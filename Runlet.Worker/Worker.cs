using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Executions;
using Runlet.Shared.Workflows;
using Runlet.Worker.Execution;

namespace Runlet.Worker;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IWorkflowStepExecutorFactory stepExecutorFactory,
    ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

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

                var run = await TryClaimNextRunAsync(dbContext, stoppingToken);
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

    private async Task<WorkflowRun?> TryClaimNextRunAsync(
        RunletDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var run = await dbContext.WorkflowRuns
            .FromSqlRaw("""
                SELECT *
                FROM workflow_runs
                WHERE status = 'Pending'
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        run.Status = WorkflowRunStatus.Running;
        run.StartedAt = now;
        run.ClaimedAt = now;
        run.ClaimedByWorkerId = workerId;
        run.LastHeartbeatAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Worker {WorkerId} claimed run {RunId}.", workerId, run.Id);

        return run;
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
        var heartbeatTask = SendHeartbeatsAsync(run.Id, heartbeatCancellation.Token);

        try
        {
            foreach (var step in orderedSteps)
            {
                if (await IsCancellationRequestedAsync(dbContext, run, cancellationToken))
                {
                    await CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                    return;
                }

                step.Status = WorkflowStepStatus.Running;
                step.StartedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                await AddLogAsync(dbContext, run.Id, step.Id, $"Starting step {step.Order}: {step.Command}", cancellationToken);

                StepExecutionResult result;
                using (var stepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (var watcherCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var executionTask = stepExecutor.ExecuteAsync(run.Image, step.Command, stepCancellation.Token);
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

                            await AddLogAsync(
                                dbContext,
                                run.Id,
                                step.Id,
                                $"Step {step.Order} completed after cancellation was requested.",
                                cancellationToken);

                            await dbContext.SaveChangesAsync(cancellationToken);
                            await CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
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

                        await AddLogAsync(
                            dbContext,
                            run.Id,
                            step.Id,
                            $"Step {step.Order} timed out after {run.StepTimeoutSeconds} seconds.",
                            cancellationToken);

                        await dbContext.SaveChangesAsync(cancellationToken);
                        await FailRunAsync(dbContext, run, step, orderedSteps, cancellationToken);
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

                        await AddLogAsync(
                            dbContext,
                            run.Id,
                            step.Id,
                            $"Step {step.Order} cancelled while running.",
                            cancellationToken);

                        await dbContext.SaveChangesAsync(cancellationToken);
                        await CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                        return;
                    }
                }

                foreach (var line in result.OutputLines)
                {
                    await AddLogAsync(dbContext, run.Id, step.Id, line, cancellationToken);
                }

                step.ExitCode = result.ExitCode;
                step.CompletedAt = DateTimeOffset.UtcNow;
                step.Status = result.ExitCode == 0
                    ? WorkflowStepStatus.Succeeded
                    : WorkflowStepStatus.Failed;

                await dbContext.SaveChangesAsync(cancellationToken);

                if (step.Status == WorkflowStepStatus.Failed)
                {
                    await FailRunAsync(dbContext, run, step, orderedSteps, cancellationToken);
                    return;
                }
            }

            if (await IsCancellationRequestedAsync(dbContext, run, cancellationToken))
            {
                await CancelRunAsync(dbContext, run, orderedSteps, cancellationToken);
                return;
            }

            run.Status = WorkflowRunStatus.Succeeded;
            run.CompletedAt = DateTimeOffset.UtcNow;

            await AddLogAsync(dbContext, run.Id, workflowStepId: null, "Run completed successfully.", cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Run {RunId} succeeded.", run.Id);
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

    private async Task SendHeartbeatsAsync(
        Guid workflowRunId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();

            await dbContext.WorkflowRuns
                .Where(run => run.Id == workflowRunId && run.Status == WorkflowRunStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        run => run.LastHeartbeatAt,
                        DateTimeOffset.UtcNow),
                    cancellationToken);

            await Task.Delay(HeartbeatInterval, cancellationToken);
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

    private async Task CancelRunAsync(
        RunletDbContext dbContext,
        WorkflowRun run,
        IReadOnlyCollection<WorkflowStep> steps,
        CancellationToken cancellationToken)
    {
        foreach (var step in steps.Where(step => step.Status == WorkflowStepStatus.Pending))
        {
            step.Status = WorkflowStepStatus.Skipped;
        }

        run.Status = WorkflowRunStatus.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;

        await AddLogAsync(dbContext, run.Id, workflowStepId: null, "Run cancelled.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Run {RunId} cancelled.", run.Id);
    }

    private async Task FailRunAsync(
        RunletDbContext dbContext,
        WorkflowRun run,
        WorkflowStep failedStep,
        IReadOnlyCollection<WorkflowStep> steps,
        CancellationToken cancellationToken)
    {
        foreach (var step in steps.Where(step => step.Status == WorkflowStepStatus.Pending))
        {
            step.Status = WorkflowStepStatus.Skipped;
        }

        run.Status = WorkflowRunStatus.Failed;
        run.CompletedAt = DateTimeOffset.UtcNow;

        await AddLogAsync(
            dbContext,
            run.Id,
            failedStep.Id,
            failedStep.ExitCode is null
                ? $"Step {failedStep.Order} failed without an exit code."
                : $"Step {failedStep.Order} failed with exit code {failedStep.ExitCode}.",
            cancellationToken);

        await AddLogAsync(dbContext, run.Id, workflowStepId: null, "Run failed.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Run {RunId} failed.", run.Id);
    }

    private static async Task AddLogAsync(
        RunletDbContext dbContext,
        Guid workflowRunId,
        Guid? workflowStepId,
        string message,
        CancellationToken cancellationToken)
    {
        dbContext.WorkflowLogEntries.Add(new WorkflowLogEntry
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            Message = message
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

}
