using Runlet.Persistence;
using Runlet.Shared.Workflows;
using Runlet.Worker.Logging;

namespace Runlet.Worker.Lifecycle;

public sealed class WorkflowRunFinalizer(
    WorkflowLogWriter logWriter,
    ILogger<WorkflowRunFinalizer> logger)
{
    public async Task SucceedRunAsync(
        RunletDbContext dbContext,
        WorkflowRun run,
        CancellationToken cancellationToken)
    {
        run.Status = WorkflowRunStatus.Succeeded;
        run.CompletedAt = DateTimeOffset.UtcNow;

        await logWriter.WriteSystemAsync(
            dbContext,
            run.Id,
            workflowStepId: null,
            "Run completed successfully.",
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Run {RunId} succeeded.", run.Id);
    }

    public async Task CancelRunAsync(
        RunletDbContext dbContext,
        WorkflowRun run,
        IReadOnlyCollection<WorkflowStep> steps,
        CancellationToken cancellationToken)
    {
        SkipPendingSteps(steps);

        run.Status = WorkflowRunStatus.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;

        await logWriter.WriteSystemAsync(
            dbContext,
            run.Id,
            workflowStepId: null,
            "Run cancelled.",
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Run {RunId} cancelled.", run.Id);
    }

    public async Task FailRunAsync(
        RunletDbContext dbContext,
        WorkflowRun run,
        WorkflowStep failedStep,
        IReadOnlyCollection<WorkflowStep> steps,
        CancellationToken cancellationToken)
    {
        SkipPendingSteps(steps);

        run.Status = WorkflowRunStatus.Failed;
        run.CompletedAt = DateTimeOffset.UtcNow;

        await logWriter.WriteSystemAsync(
            dbContext,
            run.Id,
            failedStep.Id,
            failedStep.ExitCode is null
                ? $"Step {failedStep.Order} failed without an exit code."
                : $"Step {failedStep.Order} failed with exit code {failedStep.ExitCode}.",
            cancellationToken);

        await logWriter.WriteSystemAsync(
            dbContext,
            run.Id,
            workflowStepId: null,
            "Run failed.",
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Run {RunId} failed.", run.Id);
    }

    private static void SkipPendingSteps(IReadOnlyCollection<WorkflowStep> steps)
    {
        foreach (var step in steps.Where(step => step.Status == WorkflowStepStatus.Pending))
        {
            step.Status = WorkflowStepStatus.Skipped;
        }
    }
}
