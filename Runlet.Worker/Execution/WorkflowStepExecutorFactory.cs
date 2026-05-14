using Runlet.Shared.Workflows;

namespace Runlet.Worker.Execution;

public sealed class WorkflowStepExecutorFactory(
    LocalShellWorkflowStepExecutor localShellExecutor,
    DockerWorkflowStepExecutor dockerExecutor) : IWorkflowStepExecutorFactory
{
    public IWorkflowStepExecutor GetExecutor(WorkflowExecutionMode executionMode)
    {
        return executionMode switch
        {
            WorkflowExecutionMode.LocalShell => localShellExecutor,
            WorkflowExecutionMode.Docker => dockerExecutor,
            _ => throw new InvalidOperationException($"Unknown execution mode '{executionMode}'.")
        };
    }
}
