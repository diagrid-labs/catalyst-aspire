namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

internal sealed record CliResult(string StdOut, string StdErr, int ExitCode);
