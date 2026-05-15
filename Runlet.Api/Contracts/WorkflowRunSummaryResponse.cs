using Runlet.Shared.Workflows;

namespace Runlet.Api.Contracts;

public sealed record WorkflowRunSummaryResponse(
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
    DateTimeOffset? LastHeartbeatAt,
    int StepCount,
    int SucceededStepCount,
    int FailedStepCount,
    int SkippedStepCount,
    int CancelledStepCount);
