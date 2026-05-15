namespace Runlet.Shared.Workflows;

public sealed class WorkflowStep
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid WorkflowRunId { get; init; }

    public int Order { get; init; }

    public required string Command { get; init; }

    public WorkflowStepStatus Status { get; set; } = WorkflowStepStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int? ExitCode { get; set; }
}
