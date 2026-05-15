namespace Runlet.Worker.Execution;

public interface IWorkflowStepExecutor
{
    Task<StepExecutionResult> ExecuteAsync(
        string image,
        string command,
        Func<StepOutputLine, CancellationToken, Task> onOutput,
        CancellationToken cancellationToken);
}
