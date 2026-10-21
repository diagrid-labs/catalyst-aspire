using System;
using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst;

/// <summary>Raised when the <c>diagrid</c> CLI fails, times out, or cannot be run.</summary>
public sealed class CatalystCliException : Exception
{
    public CatalystCliException(
        IReadOnlyList<string> arguments,
        int exitCode,
        string stdErr,
        string? message = null,
        Exception? innerException = null
    )
        : base(message ?? BuildMessage(arguments, exitCode, stdErr), innerException)
    {
        Arguments = arguments;
        ExitCode = exitCode;
        StdErr = stdErr;
    }

    public IReadOnlyList<string> Arguments { get; }

    public int ExitCode { get; }

    public string StdErr { get; }

    internal static string BuildMessage(
        IReadOnlyList<string> arguments,
        int exitCode,
        string stdErr,
        bool outputTruncated = false
    )
    {
        var message = $"'diagrid {string.Join(" ", arguments)}' failed with exit code {exitCode}.";
        if (!string.IsNullOrWhiteSpace(stdErr)) message += $" {stdErr.Trim()}";
        if (outputTruncated) message += " (The CLI's output was cut off, so the error text above may be incomplete.)";
        return message;
    }
}
