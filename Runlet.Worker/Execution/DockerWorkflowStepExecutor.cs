using System.Diagnostics;

namespace Runlet.Worker.Execution;

public sealed class DockerWorkflowStepExecutor : IWorkflowStepExecutor
{
    public async Task<StepExecutionResult> ExecuteAsync(
        string image,
        string command,
        Func<StepOutputLine, CancellationToken, Task> onOutput,
        CancellationToken cancellationToken)
    {
        var containerName = $"runlet-step-{Guid.NewGuid():N}";
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList =
            {
                "run",
                "--rm",
                "--name",
                containerName,
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

            await ForceRemoveContainerAsync(containerName);
            await SwallowExpectedCancellationAsync(outputTask);
            throw;
        }

        return new StepExecutionResult(process.ExitCode);
    }

    private static async Task ForceRemoveContainerAsync(string containerName)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList =
            {
                "rm",
                "-f",
                containerName
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        process.Start();
        await process.WaitForExitAsync(CancellationToken.None);
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
