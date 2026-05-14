using Runlet.Shared.Workflows;

namespace Runlet.Worker.Execution;

public interface IWorkflowStepExecutorFactory
{
    IWorkflowStepExecutor GetExecutor(WorkflowExecutionMode executionMode);
}
