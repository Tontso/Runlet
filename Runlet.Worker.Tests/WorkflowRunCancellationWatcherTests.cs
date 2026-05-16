using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Runlet.Persistence;
using Runlet.Shared.Workflows;
using Runlet.Worker.Cancellation;
using Xunit;

namespace Runlet.Worker.Tests;

public sealed class WorkflowRunCancellationWatcherTests
{
    [Fact]
    public async Task IsCancellationRequestedAsync_ReloadsRunAndReturnsTrueWhenCancellationWasRequested()
    {
        var databaseName = $"runlet-cancellation-tests-{Guid.NewGuid()}";
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var dbContext = TestRunletDbContextFactory.Create(databaseName, databaseRoot);
        var run = new WorkflowRun
        {
            Image = "alpine:latest",
            Status = WorkflowRunStatus.Running
        };

        dbContext.WorkflowRuns.Add(run);
        await dbContext.SaveChangesAsync();

        var detachedCopy = await dbContext.WorkflowRuns.FindAsync(run.Id);
        Assert.NotNull(detachedCopy);

        await using (var otherDbContext = TestRunletDbContextFactory.Create(databaseName, databaseRoot))
        {
            var sameRun = await otherDbContext.WorkflowRuns.FindAsync(run.Id);
            Assert.NotNull(sameRun);

            sameRun.CancellationRequestedAt = DateTimeOffset.UtcNow;
            await otherDbContext.SaveChangesAsync();
        }

        var watcher = new WorkflowRunCancellationWatcher(CreateScopeFactory(dbContext));

        var cancellationRequested = await watcher.IsCancellationRequestedAsync(
            dbContext,
            detachedCopy,
            CancellationToken.None);

        Assert.True(cancellationRequested);
        Assert.NotNull(detachedCopy.CancellationRequestedAt);
    }

    private static IServiceScopeFactory CreateScopeFactory(RunletDbContext dbContext)
    {
        var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();

        return services.GetRequiredService<IServiceScopeFactory>();
    }
}
