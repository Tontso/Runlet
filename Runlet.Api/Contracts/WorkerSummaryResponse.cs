namespace Runlet.Api.Contracts;

public sealed record WorkerSummaryResponse(
    string WorkerId,
    int ActiveRunCount,
    DateTimeOffset? LastHeartbeatAt,
    IReadOnlyList<WorkerRunResponse> Runs);
