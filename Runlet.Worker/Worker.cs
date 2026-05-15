using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Runlet.Persistence;
using Runlet.Shared.Workflows;
using Runlet.Worker.Cancellation;
using Runlet.Worker.Claiming;
using Runlet.Worker.Execution;
using Runlet.Worker.Heartbeats;
using Runlet.Worker.Lifecycle;
using Runlet.Worker.Logging;

namespace Runlet.Worker;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    WorkflowRunClaimer runClaimer,
    WorkflowRunCancellationWatcher cancellationWatcher,
    IWorkflowStepExecutorFactory stepExecutorFactory,
    WorkflowRunHeartbeat runHeartbeat,
    WorkflowRunFinalizer runFinalizer,
    WorkflowLogWriter logWriter,
    IOptions<WorkerOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var maxConcurrentRuns = Math.Max(1, options.Value.MaxConcurrentRuns);

        logger.LogInformation(
            "Runlet worker {WorkerId} started with {MaxConcurrentRuns} run slot(s).",
            workerId,
            maxConcurrentRuns);

        var runSlots = Enumerable
            .Range(1, maxConcurrentRuns)
            .Select(slotNumber => ExecuteRunSlotAsync(slotNumber, stoppingToken))
            .ToArray();

        try
        {
            await Task.WhenAll(runSlots);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        logger.LogInformation("Runlet worker {WorkerId} stopped.", workerId);
    }

    private async Task ExecuteRunSlotAsync(
        int slotNumber,
        CancellationToken stoppingToken)
    {
        logger.LogInformation("Runlet worker {WorkerId} slot {SlotNumber} started.", workerId, slotNumber);

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

                logger.LogInformation(
                    "Runlet worker {WorkerId} slot {SlotNumber} executing run {RunId}.",
                    workerId,
                    slotNumber,
                    run.Id);

                await ExecuteRunAsync(dbContext, run, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Worker {WorkerId} slot {SlotNumber} loop failed.",
                    workerId,
                    slotNumber);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("Runlet worker {WorkerId} slot {SlotNumber} stopped.", workerId, slotNumber);
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
                if (await cancellationWatcher.IsCancellationRequestedAsync(dbContext, run, cancellationToken))
                {
                    await runFinalizer.CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                    return;
                }

                while (true)
                {
                    if (await cancellationWatcher.IsCancellationRequestedAsync(dbContext, run, cancellationToken))
                    {
                        await runFinalizer.CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                        return;
                    }

                    step.Status = WorkflowStepStatus.Running;
                    step.AttemptCount++;
                    step.StartedAt = DateTimeOffset.UtcNow;
                    step.CompletedAt = null;
                    step.ExitCode = null;
                    await dbContext.SaveChangesAsync(cancellationToken);

                    await logWriter.WriteSystemAsync(
                        dbContext,
                        run.Id,
                        step.Id,
                        GetStartingStepMessage(step, run.MaxRetries),
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
                        var cancellationRequestTask = cancellationWatcher.WaitForCancellationRequestAsync(
                            run.Id,
                            watcherCancellation.Token);

                        var completedTask = await Task.WhenAny(executionTask, timeoutTask, cancellationRequestTask);

                        if (completedTask == executionTask)
                        {
                            await watcherCancellation.CancelAsync();
                            result = await executionTask;

                            if (await cancellationWatcher.IsCancellationRequestedAsync(dbContext, run, cancellationToken))
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
                        if (step.AttemptCount <= run.MaxRetries)
                        {
                            await logWriter.WriteSystemAsync(
                                dbContext,
                                run.Id,
                                step.Id,
                                GetRetryMessage(step, run),
                                cancellationToken);

                            if (run.RetryDelaySeconds > 0)
                            {
                                using var retryWatcherCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                var retryDelayTask = Task.Delay(
                                    TimeSpan.FromSeconds(run.RetryDelaySeconds),
                                    retryWatcherCancellation.Token);
                                var cancellationRequestTask = cancellationWatcher.WaitForCancellationRequestAsync(
                                    run.Id,
                                    retryWatcherCancellation.Token);

                                var completedTask = await Task.WhenAny(retryDelayTask, cancellationRequestTask);
                                if (completedTask == cancellationRequestTask)
                                {
                                    await cancellationRequestTask;
                                    await runFinalizer.CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                                    return;
                                }

                                await retryWatcherCancellation.CancelAsync();
                                await SwallowExpectedCancellationAsync(cancellationRequestTask);
                            }

                            continue;
                        }

                        await runFinalizer.FailRunAsync(dbContext, run, step, orderedSteps, cancellationToken);
                        return;
                    }

                    break;
                }
            }

            if (await cancellationWatcher.IsCancellationRequestedAsync(dbContext, run, cancellationToken))
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

    private static string GetStartingStepMessage(
        WorkflowStep step,
        int maxRetries)
    {
        if (maxRetries == 0)
        {
            return $"Starting step {step.Order}: {step.Command}";
        }

        return $"Starting step {step.Order} attempt {step.AttemptCount}/{maxRetries + 1}: {step.Command}";
    }

    private static string GetRetryMessage(
        WorkflowStep step,
        WorkflowRun run)
    {
        var message = $"Step {step.Order} failed with exit code {step.ExitCode}. Retrying attempt {step.AttemptCount + 1}/{run.MaxRetries + 1}.";

        return run.RetryDelaySeconds == 0
            ? message
            : $"{message} Waiting {run.RetryDelaySeconds} seconds before retry.";
    }

}
