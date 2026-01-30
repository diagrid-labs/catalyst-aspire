using System;
using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record ProjectEndpointDetails
{
    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("url")]
    public Uri Uri { get; init; }
}