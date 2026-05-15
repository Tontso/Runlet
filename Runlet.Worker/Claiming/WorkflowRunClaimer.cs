using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Workflows;

namespace Runlet.Worker.Claiming;

public sealed class WorkflowRunClaimer(ILogger<WorkflowRunClaimer> logger)
{
    public async Task<WorkflowRun?> TryClaimNextRunAsync(
        RunletDbContext dbContext,
        string workerId,
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
}
