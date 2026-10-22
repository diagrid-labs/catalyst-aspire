using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Diagrid.Aspire.Hosting.Catalyst;

internal static partial class CatalystNames
{
    private const int MaxLabelLength = 63;
    private const int HashLength = 8;

    /// <summary>
    ///     Rejects an Aspire resource name Catalyst would refuse as an app id, so the error names the
    ///     resource the developer typed rather than arriving later from the CLI.
    /// </summary>
    /// <param name="resourceName">The Aspire resource name to validate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resourceName"/> is null.</exception>
    /// <exception cref="ArgumentException">The name is not valid.</exception>
    public static void EnsureValidResourceName(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);

        if (ResourceNamePattern().IsMatch(resourceName))
        {
            return;
        }

        throw new ArgumentException(
            $"The Aspire resource name '{resourceName}' cannot be used as a Catalyst app id. Names must be 1 to 63 characters: "
            + "lowercase letters, digits and '-', starting and ending with a letter or digit (for example 'test-api', not 'Test_Api').",
            nameof(resourceName));
    }

    /// <summary>
    ///     The Catalyst subscription name for an app, pub/sub component and topic: <c>{app}-{component}-{topic}</c>,
    ///     lowercased with every run of characters outside <c>[a-z0-9]</c> collapsed to <c>-</c>, so it is a DNS label.
    ///     Over 63 characters it is cut and a hash of the unsanitised triple is appended, so two long names that share a
    ///     prefix stay distinct and the same inputs always give the same name.
    /// </summary>
    public static string SubscriptionName(string resourceName, string componentName, string topic)
    {
        var label = Sanitise($"{resourceName}-{componentName}-{topic}");

        if (label.Length == 0)
        {
            throw new ArgumentException($"Cannot derive a subscription name from app '{resourceName}', component '{componentName}', topic '{topic}'.");
        }

        if (label.Length <= MaxLabelLength)
        {
            return label;
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{resourceName}\0{componentName}\0{topic}"));
        var hash = Convert.ToHexString(digest).ToLowerInvariant()[..HashLength];
        var prefix = label[..(MaxLabelLength - HashLength - 1)].TrimEnd('-');

        return $"{prefix}-{hash}";
    }

    private static string Sanitise(string value)
        => NonLabelRun().Replace(value.ToLowerInvariant(), "-").Trim('-');

    // Source-generated: the pattern is compiled at build time rather than at first use, and it matches the
    // [GeneratedRegex] StdErrCleaner already uses, so the assembly builds its regexes one way.
    [GeneratedRegex(@"^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceNamePattern();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonLabelRun();
}
