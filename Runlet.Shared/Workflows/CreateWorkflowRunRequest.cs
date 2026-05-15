namespace Runlet.Shared.Workflows;

public sealed record CreateWorkflowRunRequest(
    string Image,
    IReadOnlyList<string> Steps,
    string? Name = null,
    WorkflowExecutionMode ExecutionMode = WorkflowExecutionMode.LocalShell,
    int StepTimeoutSeconds = 300,
    int MaxRetries = 0);
