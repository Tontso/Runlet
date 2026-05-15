using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Executions;
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
            if (string.IsNullOrWhiteSpace(request.Image))
            {
                return Results.BadRequest("Workflow image is required.");
            }

            if (request.Steps.Count == 0)
            {
                return Results.BadRequest("At least one workflow step is required.");
            }

            if (request.Steps.Any(string.IsNullOrWhiteSpace))
            {
                return Results.BadRequest("Workflow steps cannot be empty.");
            }

            if (request.StepTimeoutSeconds is < 1 or > 86_400)
            {
                return Results.BadRequest("Step timeout must be between 1 and 86400 seconds.");
            }

            if (request.MaxRetries is < 0 or > 10)
            {
                return Results.BadRequest("Max retries must be between 0 and 10.");
            }

            if (request.RetryDelaySeconds is < 0 or > 3_600)
            {
                return Results.BadRequest("Retry delay must be between 0 and 3600 seconds.");
            }

            var runName = NormalizeRunName(request.Name);

            if (runName?.Length > 200)
            {
                return Results.BadRequest("Run name cannot be longer than 200 characters.");
            }

            var run = CreateRunFromRequest(request, runName);

            dbContext.WorkflowRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/runs/{run.Id}", run);
        })
        .WithName("CreateWorkflowRun");

        app.MapGet("/runs", async (
            string? status,
            string? search,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
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

            var runs = await query
                .OrderByDescending(workflowRun => workflowRun.CreatedAt)
                .Take(50)
                .Select(workflowRun => new
                {
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
                    StepCount = workflowRun.Steps.Count,
                    SucceededStepCount = workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Succeeded),
                    FailedStepCount = workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Failed),
                    SkippedStepCount = workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Skipped),
                    CancelledStepCount = workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Cancelled)
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(runs);
        })
        .WithName("ListWorkflowRuns");

        app.MapGet("/runs/{id:guid}", async (
            Guid id,
            RunletDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var run = await dbContext.WorkflowRuns
                .AsNoTracking()
                .Select(workflowRun => new
                {
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
                    workflowRun.ClaimedByWorkerId,
                    workflowRun.ClaimedAt,
                    workflowRun.LastHeartbeatAt,
                    Steps = workflowRun.Steps
                        .OrderBy(step => step.Order)
                        .Select(step => new
                        {
                            step.Id,
                            step.Order,
                            step.Command,
                            step.Status,
                            step.AttemptCount,
                            step.StartedAt,
                            step.CompletedAt,
                            step.ExitCode
                        })
                        .ToList()
                })
                .SingleOrDefaultAsync(workflowRun => workflowRun.Id == id, cancellationToken);

            if (run is null)
            {
                return Results.NotFound();
            }

            var logs = await dbContext.WorkflowLogEntries
                .AsNoTracking()
                .Where(log => log.WorkflowRunId == id)
                .OrderBy(log => log.CreatedAt)
                .Select(log => new
                {
                    log.Id,
                    log.WorkflowStepId,
                    log.CreatedAt,
                    log.Kind,
                    log.Message
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                Run = run,
                Logs = logs
            });
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
                .Where(log => log.WorkflowRunId == id)
                .OrderBy(log => log.CreatedAt)
                .Select(log => new
                {
                    log.Id,
                    log.WorkflowRunId,
                    log.WorkflowStepId,
                    log.CreatedAt,
                    log.Kind,
                    log.Message
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(logs);
        })
        .WithName("GetWorkflowRunLogs");

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
}
