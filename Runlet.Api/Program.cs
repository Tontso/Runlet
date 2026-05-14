using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
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

    var runId = Guid.NewGuid();
    var run = new WorkflowRun
    {
        Id = runId,
        Image = request.Image,
        ExecutionMode = request.ExecutionMode,
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
            workflowRun.Status,
            workflowRun.CreatedAt,
            workflowRun.StartedAt,
            workflowRun.CompletedAt,
            StepCount = workflowRun.Steps.Count,
            SucceededStepCount = workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Succeeded),
            FailedStepCount = workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Failed),
            SkippedStepCount = workflowRun.Steps.Count(step => step.Status == WorkflowStepStatus.Skipped)
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
            workflowRun.Status,
            workflowRun.CreatedAt,
            workflowRun.StartedAt,
            workflowRun.CompletedAt,
            workflowRun.ClaimedByWorkerId,
            workflowRun.ClaimedAt,
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
            log.Message
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(logs);
})
.WithName("GetWorkflowRunLogs");

await app.RunAsync();
