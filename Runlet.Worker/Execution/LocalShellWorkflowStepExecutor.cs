using System.Diagnostics;

namespace Runlet.Worker.Execution;

public sealed class LocalShellWorkflowStepExecutor : IWorkflowStepExecutor
{
    public async Task<StepExecutionResult> ExecuteAsync(
        string image,
        string command,
        Func<StepOutputLine, CancellationToken, Task> onOutput,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { "-c", command },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        process.Start();

        var outputTask = ProcessOutputStreamer.StreamAsync(process, onOutput, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await outputTask;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await SwallowExpectedCancellationAsync(outputTask);
            throw;
        }

        return new StepExecutionResult(process.ExitCode);
    }

    private static async Task SwallowExpectedCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
