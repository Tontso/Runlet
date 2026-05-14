namespace Runlet.Shared.Executions;

public sealed class WorkflowLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid WorkflowRunId { get; init; }

    public Guid? WorkflowStepId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public WorkflowLogKind Kind { get; init; } = WorkflowLogKind.System;

    public required string Message { get; init; }
}
