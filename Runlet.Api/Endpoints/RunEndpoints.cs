using Microsoft.EntityFrameworkCore;
using Runlet.Api.Contracts;
using Runlet.Api.Validation;
using Runlet.Persistence;
using Runlet.Shared.Executions;
using Runlet.Shared.Workers;
using Runlet.Shared.Workflows;

namespace Runlet.Api.Endpoints;

public static class RunEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/runs", async (
            CreateWorkflowRunRequest request,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var validationError = CreateWorkflowRunRequestValidator.Validate(request);
            if (validationError is not null)
            {
                return Results.BadRequest(validationError);
            }

            var runName = NormalizeRunName(request.Name);

            var run = CreateRunFromRequest(request, runName);

            dbContext.WorkflowRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/runs/{run.Id}", run);
        })
        .WithName("CreateWorkflowRun");

        app.MapGet("/runs", async (
            string? status,
            string? search,
            int? page,
            int? pageSize,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var requestedPage = page ?? 1;
            var requestedPageSize = pageSize ?? 50;

            if (requestedPage < 1)
            {
                return Results.BadRequest("Page must be 1 or greater.");
            }

            if (requestedPageSize is < 1 or > 100)
            {
                return Results.BadRequest("Page size must be between 1 and 100.");
            }

            var query = dbContext.WorkflowRuns.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<WorkflowRunStatus>(status, ignoreCase: true, out var workflowRunStatus))
                {
                    return Results.BadRequest("Unknown run status filter.");
                }

                query = query.Where(workflowRun => workflowRun.Status == workflowRunStatus);
            }

            var searchText = search?.Trim();
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var searchPattern = $"%{searchText}%";
                query = query.Where(workflowRun =>
                    EF.Functions.ILike(workflowRun.Name ?? string.Empty, searchPattern)
                    || workflowRun.Id.ToString().Contains(searchText));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)requestedPageSize));
            var safePage = Math.Min(requestedPage, totalPages);

            var runs = await query
                .OrderByDescending(workflowRun => workflowRun.CreatedAt)
                .Skip((safePage - 1) * requestedPageSize)
                .Take(requestedPageSize)
                .Select(workflowRun => new WorkflowRunSummaryResponse(
                    workflowRun.Id,
                    workflowRun.Name,
                    workflowRun.Image,
                    workflowRun.ExecutionMode,
                    workflowRun.StepTimeoutSeconds,
                    workflowRun.MaxRetries,
                    workflowRun.RetryDelaySeconds,
                    workflowRun.Status,
                    workflowRun.CreatedAt,
                    workflowRun.StartedAt,
                    workflowRun.CompletedAt,
                    workflowRun.CancellationRequestedAt,
                    workflowRun.LastHeartbeatAt,
                    workflowRun.Steps.Count,
                    workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Succeeded),
                    workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Failed),
                    workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Skipped),
                    workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Cancelled)))
                .ToListAsync(cancellationToken);

            return Results.Ok(new PagedWorkflowRunsResponse(
                runs,
                safePage,
                requestedPageSize,
                totalCount,
                totalPages));
        })
        .WithName("ListWorkflowRuns");

        app.MapGet("/runs/{id:guid}", async (
            Guid id,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var run = await dbContext.WorkflowRuns
                .AsNoTracking()
                .Include(workflowRun => workflowRun.Steps)
                .SingleOrDefaultAsync(workflowRun => workflowRun.Id == id, cancellationToken);

            if (run is null)
            {
                return Results.NotFound();
            }

            var logs = await dbContext.WorkflowLogEntries
                .AsNoTracking()
                .Where(log => log.WorkflowRunId == id)
                .OrderBy(log => log.CreatedAt)
                .ToListAsync(cancellationToken);

            return Results.Ok(new WorkflowRunWithLogsResponse(
                ToRunDetailResponse(run),
                logs.Select(ToRunLogResponse).ToList()));
        })
        .WithName("GetWorkflowRun");

        app.MapPost("/runs/{id:guid}/cancel", async (
            Guid id,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var run = await dbContext.WorkflowRuns
                .Include(workflowRun => workflowRun.Steps)
                .SingleOrDefaultAsync(workflowRun => workflowRun.Id == id, cancellationToken);

            if (run is null)
            {
                return Results.NotFound();
            }

            if (run.Status is WorkflowRunStatus.Succeeded or WorkflowRunStatus.Failed or WorkflowRunStatus.Cancelled)
            {
                return Results.Conflict($"Run is already {run.Status}.");
            }

            var now = DateTimeOffset.UtcNow;
            if (run.CancellationRequestedAt is null)
            {
                run.CancellationRequestedAt = now;
                dbContext.WorkflowLogEntries.Add(new WorkflowLogEntry
                {
                    WorkflowRunId = run.Id,
                    Kind = WorkflowLogKind.System,
                    Message = "Cancellation requested."
                });
            }

            if (run.Status == WorkflowRunStatus.Pending)
            {
                run.Status = WorkflowRunStatus.Cancelled;
                run.CompletedAt = now;

                foreach (var step in run.Steps.Where(step => step.Status == WorkflowStepStatus.Pending))
                {
                    step.Status = WorkflowStepStatus.Skipped;
                }

                dbContext.WorkflowLogEntries.Add(new WorkflowLogEntry
                {
                    WorkflowRunId = run.Id,
                    Kind = WorkflowLogKind.System,
                    Message = "Run cancelled."
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Accepted($"/runs/{run.Id}", new
            {
                run.Id,
                run.Status,
                run.CancellationRequestedAt
            });
        })
        .WithName("CancelWorkflowRun");

        app.MapPost("/runs/{id:guid}/fail", async (
            Guid id,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var run = await dbContext.WorkflowRuns
                .Include(workflowRun => workflowRun.Steps)
                .SingleOrDefaultAsync(workflowRun => workflowRun.Id == id, cancellationToken);

            if (run is null)
            {
                return Results.NotFound();
            }

            if (run.Status != WorkflowRunStatus.Running)
            {
                return Results.Conflict($"Run is {run.Status}; only running runs can be manually failed.");
            }

            var now = DateTimeOffset.UtcNow;
            run.Status = WorkflowRunStatus.Failed;
            run.CompletedAt = now;

            foreach (var step in run.Steps.Where(step => step.Status == WorkflowStepStatus.Running))
            {
                step.Status = WorkflowStepStatus.Failed;
                step.CompletedAt ??= now;
            }

            foreach (var step in run.Steps.Where(step => step.Status == WorkflowStepStatus.Pending))
            {
                step.Status = WorkflowStepStatus.Skipped;
            }

            dbContext.WorkflowLogEntries.Add(new WorkflowLogEntry
            {
                WorkflowRunId = run.Id,
                Kind = WorkflowLogKind.System,
                Message = "Run manually marked failed."
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Accepted($"/runs/{run.Id}", new
            {
                run.Id,
                run.Status,
                run.CompletedAt
            });
        })
        .WithName("FailWorkflowRun");

        app.MapPost("/runs/{id:guid}/rerun", async (
            Guid id,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var sourceRun = await dbContext.WorkflowRuns
                .AsNoTracking()
                .Include(workflowRun => workflowRun.Steps)
                .SingleOrDefaultAsync(workflowRun => workflowRun.Id == id, cancellationToken);

            if (sourceRun is null)
            {
                return Results.NotFound();
            }

            if (sourceRun.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running)
            {
                return Results.Conflict($"Run is {sourceRun.Status}; only completed runs can be rerun.");
            }

            var run = CloneRun(sourceRun);

            dbContext.WorkflowRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/runs/{run.Id}", run);
        })
        .WithName("RerunWorkflowRun");

        app.MapGet("/runs/{id:guid}/logs", async (
            Guid id,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var runExists = await dbContext.WorkflowRuns
                .AnyAsync(workflowRun => workflowRun.Id == id, cancellationToken);

            if (!runExists)
            {
                return Results.NotFound();
            }

            var logs = await dbContext.WorkflowLogEntries
                .AsNoTracking()
                .Where(log => log.WorkflowRunId == id)
                .OrderBy(log => log.CreatedAt)
                .ToListAsync(cancellationToken);

            return Results.Ok(logs.Select(ToLogResponse).ToList());
        })
        .WithName("GetWorkflowRunLogs");

        app.MapGet("/workers", async (
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var now = DateTimeOffset.UtcNow;
            var registrations = await dbContext.WorkerRegistrations
                .AsNoTracking()
                .OrderBy(worker => worker.MachineName)
                .ThenBy(worker => worker.WorkerId)
                .ToListAsync(cancellationToken);

            var runningRuns = await dbContext.WorkflowRuns
                .AsNoTracking()
                .Where(run => run.Status == WorkflowRunStatus.Running && run.ClaimedByWorkerId != null)
                .OrderBy(run => run.StartedAt)
                .ToListAsync(cancellationToken);

            var runningRunsByWorker = runningRuns
                .GroupBy(run => run.ClaimedByWorkerId!)
                .ToDictionary(group => group.Key, group => group.ToList());

            var workers = registrations
                .Select(registration =>
                {
                    runningRunsByWorker.Remove(registration.WorkerId, out var workerRuns);
                    workerRuns ??= [];

                    return ToWorkerSummaryResponse(
                        registration,
                        workerRuns,
                        now);
                })
                .Concat(runningRunsByWorker.Select(group => ToUnregisteredWorkerSummaryResponse(
                    group.Key,
                    group.Value,
                    now)))
                .OrderBy(worker => GetWorkerStatusOrder(worker.Status))
                .ThenByDescending(worker => worker.ActiveRunCount)
                .ThenBy(worker => worker.WorkerId)
                .ToList();

            return Results.Ok(workers);
        })
        .WithName("ListWorkers");

        return app;
    }

    private static WorkflowRun CreateRunFromRequest(
        CreateWorkflowRunRequest request,
        string? runName)
    {
        return CreateRun(
            runName,
            request.Image,
            request.ExecutionMode,
            request.StepTimeoutSeconds,
            request.MaxRetries,
            request.RetryDelaySeconds,
            request.Steps);
    }

    private static WorkflowRun CloneRun(WorkflowRun sourceRun)
    {
        return CreateRun(
            sourceRun.Name,
            sourceRun.Image,
            sourceRun.ExecutionMode,
            sourceRun.StepTimeoutSeconds,
            sourceRun.MaxRetries,
            sourceRun.RetryDelaySeconds,
            sourceRun.Steps
                .OrderBy(step => step.Order)
                .Select(step => step.Command));
    }

    private static WorkflowRun CreateRun(
        string? name,
        string image,
        WorkflowExecutionMode executionMode,
        int stepTimeoutSeconds,
        int maxRetries,
        int retryDelaySeconds,
        IEnumerable<string> commands)
    {
        var runId = Guid.NewGuid();

        return new WorkflowRun
        {
            Id = runId,
            Name = name,
            Image = image,
            ExecutionMode = executionMode,
            StepTimeoutSeconds = stepTimeoutSeconds,
            MaxRetries = maxRetries,
            RetryDelaySeconds = retryDelaySeconds,
            Steps = commands
                .Select((command, index) => new WorkflowStep
                {
                    WorkflowRunId = runId,
                    Order = index + 1,
                    Command = command
                })
                .ToList()
        };
    }

    private static string? NormalizeRunName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private static WorkflowRunDetailResponse ToRunDetailResponse(WorkflowRun run)
    {
        return new WorkflowRunDetailResponse(
            run.Id,
            run.Name,
            run.Image,
            run.ExecutionMode,
            run.StepTimeoutSeconds,
            run.MaxRetries,
            run.RetryDelaySeconds,
            run.Status,
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt,
            run.CancellationRequestedAt,
            run.ClaimedByWorkerId,
            run.ClaimedAt,
            run.LastHeartbeatAt,
            run.Steps
                .OrderBy(step => step.Order)
                .Select(ToStepResponse)
                .ToList());
    }

    private static WorkflowStepResponse ToStepResponse(WorkflowStep step)
    {
        return new WorkflowStepResponse(
            step.Id,
            step.Order,
            step.Command,
            step.Status,
            step.AttemptCount,
            step.StartedAt,
            step.CompletedAt,
            step.ExitCode);
    }

    private static WorkflowRunLogResponse ToRunLogResponse(WorkflowLogEntry log)
    {
        return new WorkflowRunLogResponse(
            log.Id,
            log.WorkflowStepId,
            log.CreatedAt,
            log.Kind,
            log.Message);
    }

    private static WorkflowLogResponse ToLogResponse(WorkflowLogEntry log)
    {
        return new WorkflowLogResponse(
            log.Id,
            log.WorkflowRunId,
            log.WorkflowStepId,
            log.CreatedAt,
            log.Kind,
            log.Message);
    }

    private static WorkerRunResponse ToWorkerRunResponse(WorkflowRun run)
    {
        return new WorkerRunResponse(
            run.Id,
            run.Name,
            run.Image,
            run.Status,
            run.StartedAt,
            run.LastHeartbeatAt);
    }

    private static WorkerSummaryResponse ToWorkerSummaryResponse(
        WorkerRegistration registration,
        IReadOnlyList<WorkflowRun> runs,
        DateTimeOffset now)
    {
        return new WorkerSummaryResponse(
            registration.WorkerId,
            registration.MachineName,
            GetWorkerStatus(registration, runs.Count, now),
            registration.MaxConcurrentRuns,
            runs.Count,
            registration.LastHeartbeatAt,
            registration.StartedAt,
            registration.StoppedAt,
            runs.Select(ToWorkerRunResponse).ToList());
    }

    private static WorkerSummaryResponse ToUnregisteredWorkerSummaryResponse(
        string workerId,
        IReadOnlyList<WorkflowRun> runs,
        DateTimeOffset now)
    {
        var lastHeartbeatAt = runs.Max(run => run.LastHeartbeatAt);

        return new WorkerSummaryResponse(
            workerId,
            workerId,
            IsStale(lastHeartbeatAt, now) ? "Stale" : "Running",
            Math.Max(1, runs.Count),
            runs.Count,
            lastHeartbeatAt,
            runs.Min(run => run.StartedAt),
            StoppedAt: null,
            runs.Select(ToWorkerRunResponse).ToList());
    }

    private static string GetWorkerStatus(
        WorkerRegistration registration,
        int activeRunCount,
        DateTimeOffset now)
    {
        if (registration.StoppedAt is not null && registration.StoppedAt >= registration.LastHeartbeatAt)
        {
            return "Offline";
        }

        if (IsStale(registration.LastHeartbeatAt, now))
        {
            return "Stale";
        }

        return activeRunCount > 0 ? "Running" : "Idle";
    }

    private static bool IsStale(DateTimeOffset? lastHeartbeatAt, DateTimeOffset now)
    {
        return lastHeartbeatAt is null || now - lastHeartbeatAt > TimeSpan.FromSeconds(20);
    }

    private static int GetWorkerStatusOrder(string status)
    {
        return status switch
        {
            "Running" => 0,
            "Idle" => 1,
            "Stale" => 2,
            "Offline" => 3,
            _ => 4
        };
    }
}
