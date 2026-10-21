using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

/// <summary>What this integration requires of the installed <c>diagrid</c> CLI, and the actionable failures for when it falls short.</summary>
internal static class CatalystCli
{
    /// <summary>
    /// The oldest CLI release this integration is known to work with.
    /// This floor is EMPIRICALLY derived, not spec-derived: the CLI's changelog does not mention when
    /// <c>--ignore-if-exists</c> arrived, so there is no documented introduction version to look up. 1.85.0 is the
    /// oldest release actually exercised, and it carries everything the integration uses: <c>app create</c> and
    /// <c>agent create</c>, <c>--ignore-if-exists</c> on <c>project create</c> and <c>app create</c>,
    /// <c>dev run --id</c>, and <c>--label</c> / <c>--annotation</c> on <c>app create</c>. Raise it only after
    /// measuring, and lower it only after testing against the older release.
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
        if (!string.IsNullOrWhiteSpace(cleanedStdErr)) message += $" (diagrid said: {cleanedStdErr.Trim()})";
        return new(arguments, exitCode, cleanedStdErr, message);
    }

    internal static CatalystCliException UnreadableVersion(IReadOnlyList<string> arguments)
        => new(
            arguments,
            0,
            string.Empty,
            $"Could not work out which version of the diagrid CLI is installed (needs {MinimumVersion} or newer): "
                + $"'diagrid version' printed no recognisable 'Version:' line. Upgrade the CLI ({DocsUrl}) and try again.");

    internal static CatalystCliException TooOld(IReadOnlyList<string> arguments, string found)
        => new(
            arguments,
            0,
            string.Empty,
            $"The diagrid CLI is version {found}, but this integration needs {MinimumVersion} or newer. "
                + "Older releases do not support '--ignore-if-exists' on 'diagrid project create' and 'diagrid app create', "
                + "which the integration relies on so that restarting your AppHost does not fail on resources that already exist. "
                + $"Upgrade the diagrid CLI ({DocsUrl}) and try again.");

    /// <summary>
    /// Reads the version from <c>diagrid version</c> output. The command has no JSON mode (<c>--output</c> is an
    /// unknown flag), but it prints a stable <c>Version: x.y.z</c> key/value line, which is read structurally by key.
    /// Returns null when no such line carries a parsable version. A pre-release or build suffix is ignored.
    /// </summary>
    internal static Version? ParseVersion(string stdOut, out string? text)
    {
        text = null;
        foreach (var line in stdOut.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || !line.AsSpan(0, colon).Trim().Equals("Version", StringComparison.OrdinalIgnoreCase)) continue;

            var value = line.Substring(colon + 1).Trim().TrimStart('v', 'V');
            text = value;
            var core = value.Split('-', '+')[0];
            return Version.TryParse(core, out var version) ? version : null;
        }

        return null;
    }

    /// <summary>Throws an actionable exception naming the missing capability when <paramref name="stdOut"/> reports a CLI below the floor.</summary>
    internal static void EnsureSupportedVersion(IReadOnlyList<string> arguments, string stdOut)
    {
        var version = ParseVersion(stdOut, out var text);
        if (version is null) throw UnreadableVersion(arguments);
        if (version < Version.Parse(MinimumVersion)) throw TooOld(arguments, text!);
    }
}
