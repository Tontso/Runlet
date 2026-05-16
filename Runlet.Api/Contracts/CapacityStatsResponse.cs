namespace Runlet.Api.Contracts;

public sealed record CapacityStatsResponse(
    int WorkerCount,
    int RunningWorkerCount,
    int IdleWorkerCount,
    int StaleWorkerCount,
    int OfflineWorkerCount,
    int UsedSlots,
    int TotalSlots,
    int FreeSlots);
