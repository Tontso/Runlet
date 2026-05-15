using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Workflows;

namespace Runlet.Worker.Heartbeats;

public sealed class WorkflowRunHeartbeat(IServiceScopeFactory scopeFactory)
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    public async Task SendAsync(
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
}
