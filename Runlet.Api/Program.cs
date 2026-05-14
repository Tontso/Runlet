using Microsoft.EntityFrameworkCore;
using Runlet.Persistence;
using Runlet.Shared.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
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

app.MapGet("/runs/{id:guid}", async (
    Guid id,
    RunletDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.WorkflowRuns
        .Include(workflowRun => workflowRun.Steps)
        .SingleOrDefaultAsync(workflowRun => workflowRun.Id == id, cancellationToken);

    return run is not null
        ? Results.Ok(run)
        : Results.NotFound();
})
.WithName("GetWorkflowRun");

await app.RunAsync();
