using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

/// <summary>Makes captured CLI stderr fit for a human-facing exception message.</summary>
internal static partial class StdErrCleaner
{
    // CSI sequences: ESC [ parameter bytes, intermediate bytes, final byte (colours, cursor movement, ...).
    [GeneratedRegex("\u001b\\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex CsiSequence();

    /// <summary>Strips CSI escape sequences and drops the CLI's unsolicited update-notice lines.</summary>
    public static string Clean(string stdErr)
    {
        if (string.IsNullOrEmpty(stdErr)) return string.Empty;

        var stripped = CsiSequence().Replace(stdErr, string.Empty);
        var kept = new StringBuilder();
        foreach (var line in stripped.Split('\n'))
        {
            if (IsUpdateNotice(line)) continue;
            if (kept.Length > 0) kept.Append('\n');
            kept.Append(line.TrimEnd('\r'));
        }

        return kept.ToString().Trim();
    }

    // SANCTIONED EXCEPTION to the "no parsing of CLI error text" rule. That rule forbids text matching that
    // decides CONTROL FLOW. This match only removes a known unsolicited notice from a message already bound
    // for a human; nothing branches on it. The CLI prints these two lines to stderr even on success, on a
    // timer (tools/cli/cmd/diagrid.go:176-184). Do not "fix" this into exit-code logic, and do not add
    // other text matches elsewhere.
    private static bool IsUpdateNotice(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("A new release of diagrid is available", StringComparison.Ordinal)
            || trimmed.StartsWith("Update with:", StringComparison.Ordinal);
    }
}
