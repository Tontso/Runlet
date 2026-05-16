using Microsoft.Extensions.Logging.Abstractions;
using Runlet.Shared.Executions;
using Runlet.Shared.Workflows;
using Runlet.Worker.Lifecycle;
using Runlet.Worker.Logging;
using Xunit;

namespace Runlet.Worker.Tests;

public sealed class WorkflowRunFinalizerTests
{
    [Fact]
    public async Task SucceedRunAsync_MarksRunSucceededAndWritesLog()
    {
        await using var dbContext = TestRunletDbContextFactory.Create();
        var run = AddRun(dbContext, WorkflowRunStatus.Running);
        var finalizer = CreateFinalizer();

        await finalizer.SucceedRunAsync(dbContext, run, CancellationToken.None);

        Assert.Equal(WorkflowRunStatus.Succeeded, run.Status);
        Assert.NotNull(run.CompletedAt);
        Assert.Contains(dbContext.WorkflowLogEntries, log =>
            log.WorkflowRunId == run.Id
            && log.WorkflowStepId is null
            && log.Kind == WorkflowLogKind.System
            && log.Message == "Run completed successfully.");
    }

    [Fact]
    public async Task CancelRunAsync_MarksRunCancelledAndSkipsPendingSteps()
    {
        await using var dbContext = TestRunletDbContextFactory.Create();
        var run = AddRun(
            dbContext,
            WorkflowRunStatus.Running,
            new WorkflowStep
            {
                Order = 1,
                Command = "echo done",
                Status = WorkflowStepStatus.Succeeded
            },
            new WorkflowStep
            {
                Order = 2,
                Command = "echo skipped",
                Status = WorkflowStepStatus.Pending
            });
        var finalizer = CreateFinalizer();

        await finalizer.CancelRunAsync(dbContext, run, run.Steps, CancellationToken.None);

        Assert.Equal(WorkflowRunStatus.Cancelled, run.Status);
        Assert.NotNull(run.CompletedAt);
        Assert.Equal(WorkflowStepStatus.Succeeded, run.Steps[0].Status);
        Assert.Equal(WorkflowStepStatus.Skipped, run.Steps[1].Status);
        Assert.Contains(dbContext.WorkflowLogEntries, log =>
            log.WorkflowRunId == run.Id
            && log.WorkflowStepId is null
            && log.Message == "Run cancelled.");
    }

    [Fact]
    public async Task FailRunAsync_MarksRunFailedSkipsPendingStepsAndWritesFailureLogs()
    {
        await using var dbContext = TestRunletDbContextFactory.Create();
        var failedStep = new WorkflowStep
        {
            Order = 1,
            Command = "exit 1",
            Status = WorkflowStepStatus.Failed,
            ExitCode = 1
        };
        var run = AddRun(
            dbContext,
            WorkflowRunStatus.Running,
            failedStep,
            new WorkflowStep
            {
                Order = 2,
                Command = "echo skipped",
                Status = WorkflowStepStatus.Pending
            });
        var finalizer = CreateFinalizer();

        await finalizer.FailRunAsync(dbContext, run, failedStep, run.Steps, CancellationToken.None);

        Assert.Equal(WorkflowRunStatus.Failed, run.Status);
        Assert.NotNull(run.CompletedAt);
        Assert.Equal(WorkflowStepStatus.Skipped, run.Steps[1].Status);
        Assert.Contains(dbContext.WorkflowLogEntries, log =>
            log.WorkflowRunId == run.Id
            && log.WorkflowStepId == failedStep.Id
            && log.Message == "Step 1 failed with exit code 1.");
        Assert.Contains(dbContext.WorkflowLogEntries, log =>
            log.WorkflowRunId == run.Id
            && log.WorkflowStepId is null
            && log.Message == "Run failed.");
    }

    private static WorkflowRunFinalizer CreateFinalizer()
    {
        return new WorkflowRunFinalizer(
            new WorkflowLogWriter(),
            NullLogger<WorkflowRunFinalizer>.Instance);
    }

    private static WorkflowRun AddRun(
        Runlet.Persistence.RunletDbContext dbContext,
        WorkflowRunStatus status,
        params WorkflowStep[] steps)
    {
        var runId = Guid.NewGuid();
        var run = new WorkflowRun
        {
            Id = runId,
            Image = "alpine:latest",
            Status = status,
            Steps = steps
                .Select((step, index) => new WorkflowStep
                {
                    Id = step.Id,
                    WorkflowRunId = runId,
                    Order = step.Order == 0 ? index + 1 : step.Order,
                    Command = step.Command,
                    Status = step.Status,
                    AttemptCount = step.AttemptCount,
                    StartedAt = step.StartedAt,
                    CompletedAt = step.CompletedAt,
                    ExitCode = step.ExitCode
                })
                .ToList()
        };

        dbContext.WorkflowRuns.Add(run);
        dbContext.SaveChanges();

        return run;
    }
}
