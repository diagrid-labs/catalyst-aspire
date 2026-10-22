using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record ProjectEndpoints
{
    [JsonPropertyName("grpc")]
    public ProjectEndpointDetails? Grpc { get; init; }

    [JsonPropertyName("http")]
    public ProjectEndpointDetails? Http { get; init; }
}
