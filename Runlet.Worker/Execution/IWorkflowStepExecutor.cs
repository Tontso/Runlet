namespace Runlet.Worker.Execution;

public interface IWorkflowStepExecutor
{
    Task<StepExecutionResult> ExecuteAsync(
        string image,
        string command,
        CancellationToken cancellationToken);
}
