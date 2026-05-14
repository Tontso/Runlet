namespace Runlet.Worker.Execution;

public interface IWorkflowStepExecutor
{
    Task<StepExecutionResult> ExecuteAsync(
        string command,
        CancellationToken cancellationToken);
}
