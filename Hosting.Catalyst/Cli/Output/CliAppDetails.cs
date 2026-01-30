using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record CliAppDetails
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("metadata")]
    public AppIdentityMetadata Metadata { get; init; } = new();

    [JsonPropertyName("spec")]
    public AppIdentitySpec Spec { get; init; } = new();

    [JsonPropertyName("status")]
    public AppIdentityStatus Status { get; init; } = new();
}