using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Workers;

namespace Runlet.Worker.Registry;

public sealed class WorkerRegistry(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkerRegistry> logger)
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    public async Task RegisterAsync(
        string workerId,
        string machineName,
        int maxConcurrentRuns,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();
        var now = DateTimeOffset.UtcNow;
        var registration = await dbContext.WorkerRegistrations
            .SingleOrDefaultAsync(worker => worker.WorkerId == workerId, cancellationToken);

        if (registration is null)
        {
            dbContext.WorkerRegistrations.Add(new WorkerRegistration
            {
                WorkerId = workerId,
                MachineName = machineName,
                MaxConcurrentRuns = maxConcurrentRuns,
                StartedAt = now,
                LastHeartbeatAt = now,
                StoppedAt = null
            });
        }
        else
        {
            registration.MachineName = machineName;
            registration.MaxConcurrentRuns = maxConcurrentRuns;
            registration.StartedAt = now;
            registration.LastHeartbeatAt = now;
            registration.StoppedAt = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Registered worker {WorkerId}.", workerId);
    }

    public async Task SendHeartbeatAsync(
        string workerId,
        int maxConcurrentRuns,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await WriteHeartbeatAsync(workerId, maxConcurrentRuns, cancellationToken);
            await Task.Delay(HeartbeatInterval, cancellationToken);
        }
    }

    public async Task MarkStoppedAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();
        var registration = await dbContext.WorkerRegistrations
            .SingleOrDefaultAsync(worker => worker.WorkerId == workerId, cancellationToken);

        if (registration is null)
        {
            return;
        }

        registration.StoppedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Marked worker {WorkerId} stopped.", workerId);
    }

    private async Task WriteHeartbeatAsync(
        string workerId,
        int maxConcurrentRuns,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();
        var registration = await dbContext.WorkerRegistrations
            .SingleOrDefaultAsync(worker => worker.WorkerId == workerId, cancellationToken);

        if (registration is null)
        {
            return;
        }

        registration.LastHeartbeatAt = DateTimeOffset.UtcNow;
        registration.MaxConcurrentRuns = maxConcurrentRuns;
        registration.StoppedAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
