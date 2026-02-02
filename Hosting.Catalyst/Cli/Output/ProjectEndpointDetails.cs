using System;
using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record ProjectEndpointDetails
{
    [JsonPropertyName("port")]
    public required int Port { get; init; }

    [JsonPropertyName("url")]
    public required Uri Uri { get; init; }
}
