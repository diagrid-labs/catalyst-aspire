using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record CliProjectDetails
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("metadata")]
    public required ProjectMetadata Metadata { get; init; }

    [JsonPropertyName("spec")]
    public required ProjectSpec Spec { get; init; }

    [JsonPropertyName("status")]
    public required CliProjectStatus Status { get; init; }
}
