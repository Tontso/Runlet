namespace Runlet.Shared.Workflows;

public sealed class WorkflowRun
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Image { get; init; }

    public WorkflowExecutionMode ExecutionMode { get; init; } = WorkflowExecutionMode.LocalShell;

    public int StepTimeoutSeconds { get; init; } = 300;

    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.Pending;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ClaimedByWorkerId { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }

    public List<WorkflowStep> Steps { get; init; } = [];
}
