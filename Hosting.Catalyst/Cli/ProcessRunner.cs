using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

/// <summary>Spawns the <c>diagrid</c> CLI without the classic process pitfalls.</summary>
internal static class ProcessRunner
{
    /// <summary>
    /// Runs <c>diagrid</c> with <paramref name="arguments"/> (each passed as exactly one argv entry),
    /// <c>--no-color</c> appended and <c>NO_COLOR=1</c> set. Stdin is redirected and closed immediately so a CLI
    /// that prompts sees EOF instead of waiting forever. Both output streams are drained while the process runs.
    /// </summary>
    public static async Task<CliResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
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

        startInfo.ArgumentList.Add("--no-color");
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
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

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
                    await ReadQuietly(stdErrTask),
                    $"'diagrid {string.Join(" ", arguments)}' timed out after {stopwatch.Elapsed.TotalSeconds:0.#}s (limit {timeout.TotalSeconds:0.#}s)."
                );
            }

            var stdOut = await stdOutTask;
            var stdErr = await stdErrTask;

            return new CliResult(stdOut, stdErr, process.ExitCode);
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Could not kill (access denied or exiting); nothing more we can do.
        }
    }

    private static async Task<string> ReadQuietly(Task<string> read)
    {
        try
        {
            return await read;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
