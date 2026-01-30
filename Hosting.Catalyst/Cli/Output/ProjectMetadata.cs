using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record ProjectMetadata
{
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("resourceVersion")]
    public int ResourceVersion { get; init; }

    [JsonPropertyName("uid")]
    public string Uid { get; init; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; init; } = string.Empty;
}