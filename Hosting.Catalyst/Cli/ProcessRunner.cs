using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

/// <summary>Spawns the <c>diagrid</c> CLI without the classic process pitfalls.</summary>
internal static class ProcessRunner
{
    /// <summary>
    /// How long to keep waiting for the output streams to reach EOF once the process has exited or been killed.
    /// A background child that inherited the pipes would otherwise keep them open indefinitely.
    /// </summary>
    internal static readonly TimeSpan DefaultDrainGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <c>diagrid</c> with <paramref name="arguments"/> (each passed as exactly one argv entry),
    /// <c>--no-color=true</c> appended and <c>NO_COLOR=1</c> set. Stdin is redirected and closed immediately so a CLI
    /// that prompts sees EOF instead of waiting forever. Both output streams are drained while the process runs.
    /// </summary>
    public static async Task<CliResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        TimeSpan? drainGrace = null
    )
    {
        var grace = drainGrace ?? DefaultDrainGrace;
        var startInfo = new ProcessStartInfo
        {
            FileName = "diagrid",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // --no-color takes a VALUE in the real CLI: a bare flag fails with "flag needs an argument".
        // =true also stays valid if the flag ever becomes a plain bool.
        startInfo.ArgumentList.Add("--no-color=true");
        startInfo.Environment["NO_COLOR"] = "1";

        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new CatalystCliException(arguments, -1, string.Empty, "Failed to start the diagrid process.");
        }
        catch (Win32Exception ex)
        {
            throw new CatalystCliException(
                arguments,
                -1,
                string.Empty,
                $"Failed to start 'diagrid {string.Join(" ", arguments)}': {ex.Message} Is the diagrid CLI installed and on the PATH?",
                ex
            );
        }

        using (process)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The process may already have exited; nothing left to close.
            }

            // Start draining both streams before waiting: a full pipe buffer would otherwise block the child.
            var stdOutPump = new StreamPump(process.StandardOutput);
            var stdErrPump = new StreamPump(process.StandardError);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                KillTree(process);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new CatalystCliException(
                    arguments,
                    -1,
                    (await DrainAsync(stdOutPump, stdErrPump, grace)).StdErr,
                    $"'diagrid {string.Join(" ", arguments)}' timed out after {stopwatch.Elapsed.TotalSeconds:0.#}s (limit {timeout.TotalSeconds:0.#}s)."
                );
            }

            // Bounded: at most one grace after exit. If a background child still holds a pipe open, accept
            // the partial output and flag it.
            var drained = await DrainAsync(stdOutPump, stdErrPump, grace);

            return new CliResult(drained.StdOut, drained.StdErr, process.ExitCode, drained.Truncated);
        }
    }

    /// <summary>Waits for both pumps to reach EOF under one shared deadline, then snapshots both buffers.</summary>
    private static async Task<(string StdOut, string StdErr, bool Truncated)> DrainAsync(
        StreamPump stdOut,
        StreamPump stdErr,
        TimeSpan grace
    )
    {
        var both = Task.WhenAll(stdOut.Completion, stdErr.Completion);
        var truncated = await Task.WhenAny(both, Task.Delay(grace)) != both;

        return (stdOut.Snapshot(), stdErr.Snapshot(), truncated);
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Best effort: already exited, access denied, or a partial tree kill (AggregateException).
            // The caller reports the timeout either way.
        }
    }

    /// <summary>Reads a stream into a buffer so the text read so far is available even if EOF never arrives.</summary>
    private sealed class StreamPump
    {
        private readonly StringBuilder _buffer = new();
        private readonly Task _task;

        public StreamPump(StreamReader reader)
        {
            _task = Task.Run(async () =>
            {
                var chunk = new char[4096];

                try
                {
                    int read;
                    while ((read = await reader.ReadAsync(chunk, 0, chunk.Length)) > 0)
                    {
                        lock (_buffer)
                        {
                            _buffer.Append(chunk, 0, read);
                        }
                    }
                }
                catch (Exception)
                {
                    // Stream closed underneath us; keep what we have.
                }
            });
        }

        public Task Completion => _task;

        public string Snapshot()
        {
            lock (_buffer)
            {
                return _buffer.ToString();
            }
        }
    }
}
