using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record CliProjectDetails
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("metadata")]
    public ProjectMetadata Metadata { get; init; } = new();

    [JsonPropertyName("spec")]
    public ProjectSpec Spec { get; init; } = new();

    [JsonPropertyName("status")]
    public CliProjectStatus Status { get; init; } = new();
}