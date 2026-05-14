namespace Runlet.Shared.Workflows;

public sealed record CreateWorkflowRunRequest(
    string Image,
    IReadOnlyList<string> Steps,
    WorkflowExecutionMode ExecutionMode = WorkflowExecutionMode.LocalShell,
    int StepTimeoutSeconds = 300);
