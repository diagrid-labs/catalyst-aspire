using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record ProjectEndpoints
{
    [JsonPropertyName("grpc")]
    public required ProjectEndpointDetails Grpc { get; init; }

    [JsonPropertyName("http")]
    public required ProjectEndpointDetails Http { get; init; }
}