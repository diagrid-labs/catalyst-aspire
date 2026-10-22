using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

/// <summary>What this integration requires of the installed <c>diagrid</c> CLI, and the actionable failures for when it falls short.</summary>
internal static class CatalystCli
{
    /// <summary>
    /// A LOWER BOUND on the CLI release, not a verified-compatible list. It is empirically derived, not
    /// spec-derived: the CLI changelog documents no introduction version for <c>--ignore-if-exists</c>.
    /// 1.85.0 comes from the project ledger as the version that carried every command and flag used here
    /// (<c>app create</c>, <c>agent create</c>, <c>--ignore-if-exists</c> on <c>project create</c> and <c>app create</c>,
    /// <c>dev run --id</c>, <c>--label</c> / <c>--annotation</c> on <c>app create</c>). It was NOT re-measured here (the
    /// local CLI has since been upgraded), and nothing is known about older releases either way.
    /// The floor is deliberately permissive: a CLI that is too old but slips under it fails loudly at the first
    /// create with the CLI's own "unknown flag" error, rather than being silently rejected here.
    /// Note: <c>diagrid version</c> has no <c>--output json</c> (unknown flag), so its plain output is read instead.
    /// </summary>
    internal const string MinimumVersion = "1.85.0";

    internal const string DocsUrl = "https://docs.diagrid.io";

    internal static CatalystCliException NotInstalled(IReadOnlyList<string> arguments, Win32Exception inner)
        => new(
            arguments,
            -1,
            string.Empty,
            "The 'diagrid' CLI was not found. This integration provisions Catalyst by running it, so install the "
                + $"Diagrid CLI, make sure it is on your PATH, and restart the AppHost. Installation instructions: {DocsUrl}",
            inner);

    internal static CatalystCliException NotLoggedIn(IReadOnlyList<string> arguments, int exitCode, string cleanedStdErr)
    {
        var message = "You are not signed in to Diagrid. Run 'diagrid login' in a terminal, then restart the AppHost.";
        if (!string.IsNullOrWhiteSpace(cleanedStdErr))
        {
            message += $" (diagrid said: {cleanedStdErr.Trim()})";
        }

        return new(arguments, exitCode, cleanedStdErr, message);
    }

    internal static string UnreadableVersionWarning(string detail)
        => $"Could not work out which version of the diagrid CLI is installed ({detail}), so the minimum-version check ({MinimumVersion}) was skipped. "
            + $"Continuing anyway: if a command later fails with an unknown flag, upgrade the CLI ({DocsUrl}).";

    internal static CatalystCliException TooOld(IReadOnlyList<string> arguments, string found)
        => new(
            arguments,
            0,
            string.Empty,
            $"The diagrid CLI is version {found}, but this integration needs {MinimumVersion} or newer. "
                + "It was not tested against older releases, and commands it relies on, such as "
                + "'diagrid project create --ignore-if-exists' and 'diagrid app create --ignore-if-exists', may be unavailable there. "
                + $"Upgrade the diagrid CLI ({DocsUrl}) and try again.");

    /// <summary>
    /// Reads the version from <c>diagrid version</c> output. The command has no JSON mode (<c>--output</c> is an
    /// unknown flag), but it prints a <c>Version: x.y.z</c> key/value line, which is read structurally by key.
    /// Returns null when no such line carries a parsable version. A pre-release or build suffix is ignored.
    /// </summary>
    internal static Version? ParseVersion(string stdOut, out string? text)
    {
        text = null;
        foreach (var line in stdOut.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || !line.AsSpan(0, colon).Trim().Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line.Substring(colon + 1).Trim().TrimStart('v', 'V');
            text = value;
            var core = value.Split('-', '+')[0];
            return Version.TryParse(core, out var version) ? version : null;
        }

        return null;
    }

    /// <summary>
    /// Throws an actionable exception naming the missing capability when <paramref name="stdOut"/> reports a CLI below the floor.
    /// Returns false, without throwing, when no version could be read: the check exists only to give a better error
    /// message, so an unrecognised output format must not become an outage. A real capability gap still surfaces at the
    /// first create, as the CLI's own "unknown flag" error.
    /// </summary>
    internal static bool EnsureSupportedVersion(IReadOnlyList<string> arguments, string stdOut)
    {
        var version = ParseVersion(stdOut, out var text);
        if (version is null)
        {
            return false;
        }

        if (version < Version.Parse(MinimumVersion))
        {
            throw TooOld(arguments, text!);
        }

        return true;
    }
}
