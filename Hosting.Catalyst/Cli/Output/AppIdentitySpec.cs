using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record AppIdentitySpec
{
    [JsonPropertyName("apiTokenRevision")]
    public int ApiTokenRevision { get; init; }

    [JsonPropertyName("healthCheck")]
    public HealthCheck HealthCheck { get; init; } = new();

    [JsonPropertyName("projectId")]
    public string ProjectId { get; init; } = string.Empty;

    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = string.Empty;
}