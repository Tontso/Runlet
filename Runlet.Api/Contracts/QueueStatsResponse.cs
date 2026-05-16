namespace Runlet.Api.Contracts;

public sealed record QueueStatsResponse(
    int Pending,
    int Running,
    int Succeeded,
    int Failed,
    int Cancelled,
    int Total);
