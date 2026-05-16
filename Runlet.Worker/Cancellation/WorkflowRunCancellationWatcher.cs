using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Workflows;

namespace Runlet.Worker.Cancellation;

public sealed class WorkflowRunCancellationWatcher(IServiceScopeFactory scopeFactory)
{
    public async Task<bool> IsCancellationRequestedAsync(
        RunletDbContext dbContext,
        WorkflowRun run,
        CancellationToken cancellationToken)
    {
        await dbContext.Entry(run).ReloadAsync(cancellationToken);
        return run.CancellationRequestedAt is not null;
    }

    public async Task WaitForCancellationRequestAsync(
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
}
