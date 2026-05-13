namespace Runlet.Shared.Workflows;

public sealed record CreateWorkflowRunRequest(
    string Image,
    IReadOnlyList<string> Steps);
