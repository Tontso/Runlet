namespace Runlet.Api.Contracts;

public sealed record WorkflowRunWithLogsResponse(
    WorkflowRunDetailResponse Run,
    IReadOnlyList<WorkflowRunLogResponse> Logs);
