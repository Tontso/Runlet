using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Executions;
using Runlet.Shared.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddRunletPersistence(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

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

    var runId = Guid.NewGuid();
    var run = new WorkflowRun
    {
        Id = runId,
        Image = request.Image,
        ExecutionMode = request.ExecutionMode,
        StepTimeoutSeconds = request.StepTimeoutSeconds,
        Steps = request.Steps
            .Select((command, index) => new WorkflowStep
            {
                WorkflowRunId = runId,
                Order = index + 1,
                Command = command
            })
            .ToList()
    };

    dbContext.WorkflowRuns.Add(run);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/runs/{run.Id}", run);
})
.WithName("CreateWorkflowRun");

app.MapGet("/runs", async (
    RunletDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var runs = await dbContext.WorkflowRuns
        .AsNoTracking()
        .OrderByDescending(workflowRun => workflowRun.CreatedAt)
        .Take(50)
        .Select(workflowRun => new
        {
            workflowRun.Id,
            workflowRun.Image,
            workflowRun.ExecutionMode,
            workflowRun.StepTimeoutSeconds,
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
            workflowRun.Image,
            workflowRun.ExecutionMode,
            workflowRun.StepTimeoutSeconds,
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

await app.RunAsync();
