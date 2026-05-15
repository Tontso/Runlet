using System.Diagnostics;
using System.Threading.Channels;
using Runlet.Shared.Executions;

namespace Runlet.Worker.Execution;

internal static class ProcessOutputStreamer
{
    public static async Task StreamAsync(
        Process process,
        Func<StepOutputLine, CancellationToken, Task> onOutput,
        CancellationToken cancellationToken)
    {
        var outputLines = Channel.CreateUnbounded<StepOutputLine>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        var stdoutTask = ReadLinesAsync(
            process.StandardOutput,
            WorkflowLogKind.Stdout,
            outputLines.Writer,
            cancellationToken);

        var stderrTask = ReadLinesAsync(
            process.StandardError,
            WorkflowLogKind.Stderr,
            outputLines.Writer,
            cancellationToken);

        var completionTask = Task.WhenAll(stdoutTask, stderrTask)
            .ContinueWith(
                task =>
                {
                    if (task.Exception is not null)
                    {
                        outputLines.Writer.TryComplete(task.Exception);
                        return;
                    }

                    outputLines.Writer.TryComplete();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        await foreach (var outputLine in outputLines.Reader.ReadAllAsync(cancellationToken))
        {
            await onOutput(outputLine, cancellationToken);
        }

        await completionTask;
    }

    private static async Task ReadLinesAsync(
        TextReader reader,
        WorkflowLogKind kind,
        ChannelWriter<StepOutputLine> writer,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return;
            }

            if (line.Length == 0)
            {
                continue;
            }

            await writer.WriteAsync(new StepOutputLine(kind, line), cancellationToken);
        }
    }
}
