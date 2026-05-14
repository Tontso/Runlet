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

        foreach (var step in orderedSteps)
        {
            step.Status = WorkflowStepStatus.Running;
            step.StartedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await AddLogAsync(dbContext, run.Id, step.Id, $"Starting step {step.Order}: {step.Command}", cancellationToken);

            var result = await stepExecutor.ExecuteAsync(run.Image, step.Command, cancellationToken);

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

        run.Status = WorkflowRunStatus.Succeeded;
        run.CompletedAt = DateTimeOffset.UtcNow;

        await AddLogAsync(dbContext, run.Id, workflowStepId: null, "Run completed successfully.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Run {RunId} succeeded.", run.Id);
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
            $"Step {failedStep.Order} failed with exit code {failedStep.ExitCode}.",
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
