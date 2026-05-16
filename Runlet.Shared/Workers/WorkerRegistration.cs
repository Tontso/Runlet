namespace Runlet.Shared.Workers;

public sealed class WorkerRegistration
{
    public required string WorkerId { get; init; }

    public required string MachineName { get; set; }

    public int MaxConcurrentRuns { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastHeartbeatAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StoppedAt { get; set; }
}
