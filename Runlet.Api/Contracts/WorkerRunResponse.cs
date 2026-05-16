using Runlet.Shared.Workflows;

namespace Runlet.Api.Contracts;

public sealed record WorkerRunResponse(
    Guid Id,
    string? Name,
    string Image,
    WorkflowRunStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastHeartbeatAt);
