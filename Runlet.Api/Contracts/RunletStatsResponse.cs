namespace Runlet.Api.Contracts;

public sealed record RunletStatsResponse(
    QueueStatsResponse Queue,
    CapacityStatsResponse Capacity);
