using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record CliProjectStatus
{
    [JsonPropertyName("endpoints")]
    public required ProjectEndpoints Endpoints { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; init; }
}
