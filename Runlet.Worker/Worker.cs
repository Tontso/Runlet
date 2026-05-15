using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Workflows;
using Runlet.Worker.Claiming;
using Runlet.Worker.Execution;
using Runlet.Worker.Heartbeats;
using Runlet.Worker.Lifecycle;
using Runlet.Worker.Logging;

namespace Runlet.Worker;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    WorkflowRunClaimer runClaimer,
    IWorkflowStepExecutorFactory stepExecutorFactory,
    WorkflowRunHeartbeat runHeartbeat,
    WorkflowRunFinalizer runFinalizer,
    WorkflowLogWriter logWriter,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Runlet worker {WorkerId} started.", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();

                var run = await runClaimer.TryClaimNextRunAsync(dbContext, workerId, stoppingToken);
                if (run is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                await ExecuteRunAsync(dbContext, run, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("Runlet worker {WorkerId} stopped.", workerId);
    }

    private async Task ExecuteRunAsync(
        RunletDbContext dbContext,
        WorkflowRun run,
        CancellationToken cancellationToken)
    {
        await dbContext.Entry(run)
            .Collection(workflowRun => workflowRun.Steps)
            .LoadAsync(cancellationToken);

        var orderedSteps = run.Steps.OrderBy(step => step.Order).ToList();
        var stepExecutor = stepExecutorFactory.GetExecutor(run.ExecutionMode);

        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = runHeartbeat.SendAsync(run.Id, heartbeatCancellation.Token);

        try
        {
            foreach (var step in orderedSteps)
            {
                if (await IsCancellationRequestedAsync(dbContext, run, cancellationToken))
                {
                    await runFinalizer.CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                    return;
                }

                step.Status = WorkflowStepStatus.Running;
                step.StartedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                await logWriter.WriteSystemAsync(
                    dbContext,
                    run.Id,
                    step.Id,
                    $"Starting step {step.Order}: {step.Command}",
                    cancellationToken);

                StepExecutionResult result;
                using (var stepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (var watcherCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var executionTask = stepExecutor.ExecuteAsync(
                        run.Image,
                        step.Command,
                        async (line, outputCancellationToken) =>
                        {
                            await logWriter.WriteAsync(
                                dbContext,
                                run.Id,
                                step.Id,
                                line.Kind,
                                line.Message,
                                outputCancellationToken);
                        },
                        stepCancellation.Token);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(run.StepTimeoutSeconds), watcherCancellation.Token);
                    var cancellationRequestTask = WaitForCancellationRequestAsync(run.Id, watcherCancellation.Token);

                    var completedTask = await Task.WhenAny(executionTask, timeoutTask, cancellationRequestTask);

                    if (completedTask == executionTask)
                    {
                        await watcherCancellation.CancelAsync();
                        result = await executionTask;

                        if (await IsCancellationRequestedAsync(dbContext, run, cancellationToken))
                        {
                            step.CompletedAt = DateTimeOffset.UtcNow;
                            step.Status = WorkflowStepStatus.Cancelled;

                            await logWriter.WriteSystemAsync(
                                dbContext,
                                run.Id,
                                step.Id,
                                $"Step {step.Order} completed after cancellation was requested.",
                                cancellationToken);

                            await dbContext.SaveChangesAsync(cancellationToken);
                            await runFinalizer.CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                            return;
                        }
                    }

                    else if (completedTask == timeoutTask)
                    {
                        await watcherCancellation.CancelAsync();
                        await stepCancellation.CancelAsync();
                        await SwallowExpectedCancellationAsync(executionTask);

                        step.CompletedAt = DateTimeOffset.UtcNow;
                        step.Status = WorkflowStepStatus.Failed;

                        await logWriter.WriteSystemAsync(
                            dbContext,
                            run.Id,
                            step.Id,
                            $"Step {step.Order} timed out after {run.StepTimeoutSeconds} seconds.",
                            cancellationToken);

                        await dbContext.SaveChangesAsync(cancellationToken);
                        await runFinalizer.FailRunAsync(dbContext, run, step, orderedSteps, cancellationToken);
                        return;
                    }

                    else
                    {
                        await cancellationRequestTask;
                        await watcherCancellation.CancelAsync();
                        await stepCancellation.CancelAsync();
                        await SwallowExpectedCancellationAsync(executionTask);

                        step.CompletedAt = DateTimeOffset.UtcNow;
                        step.Status = WorkflowStepStatus.Cancelled;

                        await logWriter.WriteSystemAsync(
                            dbContext,
                            run.Id,
                            step.Id,
                            $"Step {step.Order} cancelled while running.",
                            cancellationToken);

                        await dbContext.SaveChangesAsync(cancellationToken);
                        await runFinalizer.CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                        return;
                    }
                }

                step.ExitCode = result.ExitCode;
                step.CompletedAt = DateTimeOffset.UtcNow;
                step.Status = result.ExitCode == 0
                    ? WorkflowStepStatus.Succeeded
                    : WorkflowStepStatus.Failed;

                await dbContext.SaveChangesAsync(cancellationToken);

                if (step.Status == WorkflowStepStatus.Failed)
                {
                    await runFinalizer.FailRunAsync(dbContext, run, step, orderedSteps, cancellationToken);
                    return;
                }
            }

            if (await IsCancellationRequestedAsync(dbContext, run, cancellationToken))
            {
                await runFinalizer.CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                return;
            }

            await runFinalizer.SucceedRunAsync(dbContext, run, cancellationToken);
        }
        finally
        {
            await heartbeatCancellation.CancelAsync();
            await SwallowExpectedCancellationAsync(heartbeatTask);
        }
    }

    private static async Task<bool> IsCancellationRequestedAsync(
        RunletDbContext dbContext,
        WorkflowRun run,
        CancellationToken cancellationToken)
    {
        await dbContext.Entry(run).ReloadAsync(cancellationToken);
        return run.CancellationRequestedAt is not null;
    }

    private async Task WaitForCancellationRequestAsync(
        Guid workflowRunId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();
            var cancellationRequested = await dbContext.WorkflowRuns
                .AsNoTracking()
                .Where(run => run.Id == workflowRunId)
                .Select(run => run.CancellationRequestedAt != null)
                .SingleAsync(cancellationToken);

            if (cancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task SwallowExpectedCancellationAsync(Task executionTask)
    {
        try
        {
            await executionTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

}
