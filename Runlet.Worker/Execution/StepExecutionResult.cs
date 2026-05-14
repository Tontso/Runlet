namespace Runlet.Worker.Execution;

public sealed record StepExecutionResult(
    int ExitCode,
    IReadOnlyList<StepOutputLine> OutputLines);
