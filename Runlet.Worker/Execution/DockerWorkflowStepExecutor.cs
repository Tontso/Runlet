using System.Diagnostics;

namespace Runlet.Worker.Execution;

public sealed class DockerWorkflowStepExecutor : IWorkflowStepExecutor
{
    public async Task<StepExecutionResult> ExecuteAsync(
        string image,
        string command,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList =
            {
                "run",
                "--rm",
                image,
                "/bin/sh",
                "-c",
                command
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var outputLines = stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Concat(stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        return new StepExecutionResult(process.ExitCode, outputLines);
    }
}
