using Runlet.Shared.Executions;

namespace Runlet.Worker.Execution;

public sealed record StepOutputLine(
    WorkflowLogKind Kind,
    string Message);
