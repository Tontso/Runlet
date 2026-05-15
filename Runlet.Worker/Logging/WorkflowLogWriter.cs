using Runlet.Persistence;
using Runlet.Shared.Executions;

namespace Runlet.Worker.Logging;

public sealed class WorkflowLogWriter
{
    public async Task WriteSystemAsync(
        RunletDbContext dbContext,
        Guid workflowRunId,
        Guid? workflowStepId,
        string message,
        CancellationToken cancellationToken)
    {
        await WriteAsync(
            dbContext,
            workflowRunId,
            workflowStepId,
            WorkflowLogKind.System,
            message,
            cancellationToken);
    }

    public async Task WriteAsync(
        RunletDbContext dbContext,
        Guid workflowRunId,
        Guid? workflowStepId,
        WorkflowLogKind kind,
        string message,
        CancellationToken cancellationToken)
    {
        dbContext.WorkflowLogEntries.Add(new WorkflowLogEntry
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            Kind = kind,
            Message = message
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
