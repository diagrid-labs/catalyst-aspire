using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record AppIdentityStatus
{
    [JsonPropertyName("apiToken")]
    public string ApiToken { get; init; } = string.Empty;

    [JsonPropertyName("spiffeId")]
    public string SpiffeId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; init; } = string.Empty;
}