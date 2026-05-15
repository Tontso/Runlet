using Runlet.Shared.Executions;

namespace Runlet.Api.Contracts;

public sealed record WorkflowLogResponse(
    Guid Id,
    Guid WorkflowRunId,
    Guid? WorkflowStepId,
    DateTimeOffset CreatedAt,
    WorkflowLogKind Kind,
    string Message);
