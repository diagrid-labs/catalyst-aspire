using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Options;

/// <summary>
/// Options for <c>app create</c> and <c>agent create</c>. Agents accept only the project, endpoint, workflow
/// archive, wait and ignore-if-exists options; the rest are applied to apps only.
/// </summary>
public record CreateIdentityOptions
{
    public required string Project { get; init; }
    public string? Endpoint { get; init; }
    public string? EndpointToken { get; init; }
    public string? EndpointProtocol { get; init; }
    public string? AppConfig { get; init; }
    public string? MaxBodySize { get; init; }
    public bool EnableAppHealthCheck { get; init; }
    public string? AppHealthCheckPath { get; init; }
    public int? AppHealthProbeInterval { get; init; }
    public int? AppHealthProbeTimeout { get; init; }
    public int? AppHealthThreshold { get; init; }
    public int? AppChannelTimeoutSeconds { get; init; }
    /// <summary>Sent as <c>--label</c> (pflag <c>stringToString</c>, which splits on commas: an embedded <c>=</c> survives, but a comma in a value would be mis-split).</summary>
    public IDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    /// <summary>Sent as <c>--annotation</c> (pflag <c>stringToString</c>, which splits on commas: an embedded <c>=</c> survives, but a comma in a value would be mis-split).</summary>
    public IDictionary<string, string> Annotations { get; init; } = new Dictionary<string, string>();
    public IList<string> EnableWorkflowArchive { get; init; } = [];
    public IList<string> EnableWorkflowNotifications { get; init; } = [];
    public bool Wait { get; init; } = true;
    public bool IgnoreIfExists { get; init; } = true;
}
