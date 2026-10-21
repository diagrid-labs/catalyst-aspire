using System;
using System.Text.RegularExpressions;

namespace Diagrid.Aspire.Hosting.Catalyst;

internal static class CatalystNames
{
    private static readonly Regex ResourceNamePattern = new(@"^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Rejects an Aspire resource name Catalyst would refuse as an app id, so the error names the
    ///     resource the developer typed rather than arriving later from the CLI.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not valid.</exception>
    public static void EnsureValidResourceName(string name)
    {
        if (name is not null && ResourceNamePattern.IsMatch(name))
            return;

        throw new ArgumentException(
            $"The Aspire resource name '{name}' cannot be used as a Catalyst app id. Names must be 1 to 63 characters: "
            + "lowercase letters, digits and '-', starting and ending with a letter or digit (for example 'test-api', not 'Test_Api').",
            nameof(name));
    }
}
