namespace Runlet.Api.Contracts;

public sealed record PagedWorkflowRunsResponse(
    IReadOnlyList<WorkflowRunSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
