using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Runlet.Api.Contracts;
using Runlet.Persistence;
using Runlet.Shared.Executions;
using Runlet.Shared.Workers;
using Runlet.Shared.Workflows;
using Xunit;

namespace Runlet.Api.Tests;

public sealed class RunEndpointsTests(RunletApiFactory factory) :
    IClassFixture<RunletApiFactory>,
    IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public Task InitializeAsync()
    {
        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateRun_CreatesPendingRunWithSteps()
    {
        var client = factory.CreateClient();
        var request = new CreateWorkflowRunRequest(
            "alpine:latest",
            ["echo hello", "echo done"],
            Name: "smoke",
            ExecutionMode: WorkflowExecutionMode.Docker,
            StepTimeoutSeconds: 120,
            MaxRetries: 1,
            RetryDelaySeconds: 5);

        var response = await client.PostAsJsonAsync("/runs", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var run = await response.Content.ReadFromJsonAsync<WorkflowRun>(JsonOptions);
        Assert.NotNull(run);
        Assert.Equal("smoke", run.Name);
        Assert.Equal("alpine:latest", run.Image);
        Assert.Equal(WorkflowExecutionMode.Docker, run.ExecutionMode);
        Assert.Equal(WorkflowRunStatus.Pending, run.Status);
        Assert.Equal(2, run.Steps.Count);
        Assert.All(run.Steps, step => Assert.Equal(WorkflowStepStatus.Pending, step.Status));
    }

    [Fact]
    public async Task CreateRun_WithInvalidRequest_ReturnsBadRequest()
    {
        var client = factory.CreateClient();
        var request = new CreateWorkflowRunRequest("", []);

        var response = await client.PostAsJsonAsync("/runs", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListRuns_ReturnsPagedRuns()
    {
        var client = factory.CreateClient();

        await CreateRunAsync(client, "run-1");
        await CreateRunAsync(client, "run-2");
        await CreateRunAsync(client, "run-3");

        var response = await client.GetAsync("/runs?page=2&pageSize=2");

        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PagedWorkflowRunsResponse>(JsonOptions);
        Assert.NotNull(page);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task GetRun_ReturnsRunWithOrderedStepsAndLogs()
    {
        var client = factory.CreateClient();
        var run = await CreateRunAsync(client, "detail");
        var secondStep = run.Steps.Single(step => step.Order == 2);

        await AddLogAsync(run.Id, secondStep.Id, "hello from step 2");

        var response = await client.GetAsync($"/runs/{run.Id}");

        response.EnsureSuccessStatusCode();

        var detail = await response.Content.ReadFromJsonAsync<WorkflowRunWithLogsResponse>(JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(run.Id, detail.Run.Id);
        Assert.Equal("detail", detail.Run.Name);
        Assert.Collection(
            detail.Run.Steps,
            step => Assert.Equal(1, step.Order),
            step => Assert.Equal(2, step.Order));
        Assert.Single(detail.Logs);
        Assert.Equal("hello from step 2", detail.Logs[0].Message);
        Assert.Equal(secondStep.Id, detail.Logs[0].WorkflowStepId);
    }

    [Fact]
    public async Task CancelRun_WhenPending_CancelsRunAndSkipsSteps()
    {
        var client = factory.CreateClient();
        var run = await CreateRunAsync(client, "cancel-me");

        var response = await client.PostAsync($"/runs/{run.Id}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var detail = await client.GetFromJsonAsync<WorkflowRunWithLogsResponse>($"/runs/{run.Id}", JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(WorkflowRunStatus.Cancelled, detail.Run.Status);
        Assert.NotNull(detail.Run.CancellationRequestedAt);
        Assert.All(detail.Run.Steps, step => Assert.Equal(WorkflowStepStatus.Skipped, step.Status));
        Assert.Contains(detail.Logs, log => log.Message == "Run cancelled.");
    }

    [Fact]
    public async Task Rerun_WhenSourceIsCompleted_ClonesRunAsPending()
    {
        var client = factory.CreateClient();
        var sourceRun = await AddCompletedRunAsync();

        var response = await client.PostAsync($"/runs/{sourceRun.Id}/rerun", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var rerun = await response.Content.ReadFromJsonAsync<WorkflowRun>(JsonOptions);
        Assert.NotNull(rerun);
        Assert.NotEqual(sourceRun.Id, rerun.Id);
        Assert.Equal(sourceRun.Name, rerun.Name);
        Assert.Equal(sourceRun.Image, rerun.Image);
        Assert.Equal(sourceRun.ExecutionMode, rerun.ExecutionMode);
        Assert.Equal(sourceRun.StepTimeoutSeconds, rerun.StepTimeoutSeconds);
        Assert.Equal(sourceRun.MaxRetries, rerun.MaxRetries);
        Assert.Equal(sourceRun.RetryDelaySeconds, rerun.RetryDelaySeconds);
        Assert.Equal(WorkflowRunStatus.Pending, rerun.Status);
        Assert.Equal(
            sourceRun.Steps.OrderBy(step => step.Order).Select(step => step.Command),
            rerun.Steps.OrderBy(step => step.Order).Select(step => step.Command));
    }

    [Fact]
    public async Task ListWorkers_ReturnsActiveWorkersGroupedByClaimedRunningRuns()
    {
        var client = factory.CreateClient();

        await AddWorkerRegistrationAsync("worker-a", "machine-a", 2, DateTimeOffset.UtcNow.AddSeconds(-4));
        await AddWorkerRegistrationAsync("worker-b", "machine-b", 1, DateTimeOffset.UtcNow.AddSeconds(-8));
        await AddWorkerRegistrationAsync("worker-c", "machine-c", 3, DateTimeOffset.UtcNow.AddSeconds(-2));
        await AddClaimedRunAsync("worker-a", "run-a1", DateTimeOffset.UtcNow.AddSeconds(-4));
        await AddClaimedRunAsync("worker-a", "run-a2", DateTimeOffset.UtcNow.AddSeconds(-1));
        await AddClaimedRunAsync("worker-b", "run-b1", DateTimeOffset.UtcNow.AddSeconds(-8));
        await AddClaimedRunAsync("worker-c", "completed", DateTimeOffset.UtcNow, WorkflowRunStatus.Succeeded);

        var response = await client.GetAsync("/workers");

        response.EnsureSuccessStatusCode();

        var workers = await response.Content.ReadFromJsonAsync<IReadOnlyList<WorkerSummaryResponse>>(JsonOptions);
        Assert.NotNull(workers);
        Assert.Equal(3, workers.Count);

        var workerA = Assert.Single(workers, worker => worker.WorkerId == "worker-a");
        Assert.Equal("machine-a", workerA.MachineName);
        Assert.Equal("Running", workerA.Status);
        Assert.Equal(2, workerA.MaxConcurrentRuns);
        Assert.Equal(2, workerA.ActiveRunCount);
        Assert.Equal(["run-a1", "run-a2"], workerA.Runs.Select(run => run.Name).Order());
        Assert.NotNull(workerA.LastHeartbeatAt);

        var workerB = Assert.Single(workers, worker => worker.WorkerId == "worker-b");
        Assert.Equal(1, workerB.ActiveRunCount);
        Assert.Equal("run-b1", Assert.Single(workerB.Runs).Name);

        var workerC = Assert.Single(workers, worker => worker.WorkerId == "worker-c");
        Assert.Equal("Idle", workerC.Status);
        Assert.Equal(0, workerC.ActiveRunCount);
        Assert.Empty(workerC.Runs);
    }

    private static async Task<WorkflowRun> CreateRunAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/runs",
            new CreateWorkflowRunRequest(
                "alpine:latest",
                ["echo hello", "echo done"],
                Name: name,
                ExecutionMode: WorkflowExecutionMode.LocalShell,
                StepTimeoutSeconds: 90,
                MaxRetries: 1,
                RetryDelaySeconds: 3),
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var run = await response.Content.ReadFromJsonAsync<WorkflowRun>(JsonOptions);
        Assert.NotNull(run);

        return run;
    }

    private async Task AddLogAsync(Guid runId, Guid stepId, string message)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();

        dbContext.WorkflowLogEntries.Add(new WorkflowLogEntry
        {
            WorkflowRunId = runId,
            WorkflowStepId = stepId,
            Message = message
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<WorkflowRun> AddCompletedRunAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();
        var runId = Guid.NewGuid();

        var run = new WorkflowRun
        {
            Id = runId,
            Name = "completed",
            Image = "alpine:latest",
            ExecutionMode = WorkflowExecutionMode.Docker,
            StepTimeoutSeconds = 45,
            MaxRetries = 2,
            RetryDelaySeconds = 10,
            Status = WorkflowRunStatus.Succeeded,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-3),
            CompletedAt = DateTimeOffset.UtcNow,
            Steps =
            [
                new WorkflowStep
                {
                    WorkflowRunId = runId,
                    Order = 1,
                    Command = "echo first",
                    Status = WorkflowStepStatus.Succeeded,
                    ExitCode = 0
                },
                new WorkflowStep
                {
                    WorkflowRunId = runId,
                    Order = 2,
                    Command = "echo second",
                    Status = WorkflowStepStatus.Succeeded,
                    ExitCode = 0
                }
            ]
        };

        dbContext.WorkflowRuns.Add(run);
        await dbContext.SaveChangesAsync();

        return run;
    }

    private async Task AddClaimedRunAsync(
        string workerId,
        string name,
        DateTimeOffset lastHeartbeatAt,
        WorkflowRunStatus status = WorkflowRunStatus.Running)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();

        dbContext.WorkflowRuns.Add(new WorkflowRun
        {
            Name = name,
            Image = "alpine:latest",
            Status = status,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            ClaimedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            ClaimedByWorkerId = workerId,
            LastHeartbeatAt = lastHeartbeatAt,
            Steps =
            [
                new WorkflowStep
                {
                    Order = 1,
                    Command = "sleep 30",
                    Status = status == WorkflowRunStatus.Running
                        ? WorkflowStepStatus.Running
                        : WorkflowStepStatus.Succeeded
                }
            ]
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task AddWorkerRegistrationAsync(
        string workerId,
        string machineName,
        int maxConcurrentRuns,
        DateTimeOffset lastHeartbeatAt)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RunletDbContext>();

        dbContext.WorkerRegistrations.Add(new WorkerRegistration
        {
            WorkerId = workerId,
            MachineName = machineName,
            MaxConcurrentRuns = maxConcurrentRuns,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastHeartbeatAt = lastHeartbeatAt
        });

        await dbContext.SaveChangesAsync();
    }
}
