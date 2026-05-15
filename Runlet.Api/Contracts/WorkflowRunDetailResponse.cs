using Runlet.Shared.Workflows;

namespace Runlet.Api.Contracts;

public sealed record WorkflowRunDetailResponse(
    Guid Id,
    string? Name,
    string Image,
    WorkflowExecutionMode ExecutionMode,
    int StepTimeoutSeconds,
    int MaxRetries,
    int RetryDelaySeconds,
    WorkflowRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancellationRequestedAt,
    string? ClaimedByWorkerId,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? LastHeartbeatAt,
    IReadOnlyList<WorkflowStepResponse> Steps);
