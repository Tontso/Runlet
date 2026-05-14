using System.Diagnostics;
using Runlet.Shared.Executions;

namespace Runlet.Worker.Execution;

public sealed class DockerWorkflowStepExecutor : IWorkflowStepExecutor
{
    public async Task<StepExecutionResult> ExecuteAsync(
        string image,
        string command,
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

            await ForceRemoveContainerAsync(containerName);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var outputLines = stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new StepOutputLine(WorkflowLogKind.Stdout, line))
            .Concat(stderr
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => new StepOutputLine(WorkflowLogKind.Stderr, line)))
            .ToList();

        return new StepExecutionResult(process.ExitCode, outputLines);
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
}
