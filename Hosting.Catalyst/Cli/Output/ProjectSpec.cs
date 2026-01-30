using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record ProjectSpec
{
    [JsonPropertyName("defaultWorkflowStoreEnabled")]
    public bool DefaultWorkflowStoreEnabled { get; init; }

    [JsonPropertyName("disableAppTunnels")]
    public bool DisableAppTunnels { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("privateRegion")]
    public bool PrivateRegion { get; init; }

    [JsonPropertyName("region")]
    public string Region { get; init; } = string.Empty;
}