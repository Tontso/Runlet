namespace Runlet.Worker.Execution;

public sealed record StepExecutionResult(
    int ExitCode,
    IReadOnlyList<string> OutputLines);
