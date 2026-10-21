namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

/// <param name="OutputTruncated">
/// True when the CLI exited but its output pipes did not reach EOF within the drain grace (a background child
/// kept them open), so <paramref name="StdOut"/> / <paramref name="StdErr"/> may be incomplete.
/// </param>
internal sealed record CliResult(string StdOut, string StdErr, int ExitCode, bool OutputTruncated = false);
