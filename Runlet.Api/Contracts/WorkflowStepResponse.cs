using Runlet.Shared.Workflows;

namespace Runlet.Api.Contracts;

public sealed record WorkflowStepResponse(
    Guid Id,
    int Order,
    string Command,
    WorkflowStepStatus Status,
    int AttemptCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode);
