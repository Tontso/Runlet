using Runlet.Shared.Executions;

namespace Runlet.Api.Contracts;

public sealed record WorkflowRunLogResponse(
    Guid Id,
    Guid? WorkflowStepId,
    DateTimeOffset CreatedAt,
    WorkflowLogKind Kind,
    string Message);
