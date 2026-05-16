namespace Runlet.Api.Contracts;

public sealed record WorkerSummaryResponse(
    string WorkerId,
    string MachineName,
    string Status,
    int MaxConcurrentRuns,
    int ActiveRunCount,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? StoppedAt,
    IReadOnlyList<WorkerRunResponse> Runs);
