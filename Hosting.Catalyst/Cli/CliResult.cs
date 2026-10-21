using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

/// <param name="OutputTruncated">
/// True when the CLI exited but its output pipes did not reach EOF within the drain grace (a background child
/// kept them open), so <paramref name="StdOut"/> / <paramref name="StdErr"/> may be incomplete. False does not
/// prove the output is whole: a stream pump that died on an exception also reports false.
/// </param>
internal sealed record CliResult(string StdOut, string StdErr, int ExitCode, bool OutputTruncated = false)
{
    /// <summary>Throws <see cref="CatalystCliException"/> carrying the cleaned stderr when the exit code is non-zero.</summary>
    public void EnsureSuccess(IReadOnlyList<string> arguments)
    {
        if (ExitCode == 0) return;

        var cleaned = StdErrCleaner.Clean(StdErr);
        throw new CatalystCliException(
            arguments,
            ExitCode,
            cleaned,
            CatalystCliException.BuildMessage(arguments, ExitCode, cleaned, OutputTruncated));
    }
}
