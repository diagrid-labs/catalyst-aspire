using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

// Every field is omitempty on the wire: they appear only once the project is serving. Absent means "not ready yet".
public record CliProjectStatus
{
    [JsonPropertyName("endpoints")]
    public ProjectEndpoints? Endpoints { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }
}
