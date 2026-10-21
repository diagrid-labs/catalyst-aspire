using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Diagrid.Aspire.Hosting.Catalyst.Model;

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
    public CliProjectStatus? Status { get; init; }

    public static CliProjectDetails Parse(string json)
        => JsonSerializer.Deserialize<CliProjectDetails>(json)
            ?? throw new InvalidOperationException("Failed to deserialize project details");

    /// <summary>
    ///     The project's endpoints, or null while the project is still coming up (the CLI omits them until then).
    ///     Both endpoints must be present and absolute (an empty "url" parses to a relative Uri): a half-populated project is not ready.
    /// </summary>
    public ProjectDetails? ToProjectDetails()
    {
        var endpoints = Status?.Endpoints;

        if (endpoints?.Http?.Uri is not { IsAbsoluteUri: true } http || endpoints.Grpc?.Uri is not { IsAbsoluteUri: true } grpc)
        {
            return null;
        }

        return new() { HttpEndpoint = http, GrpcEndpoint = grpc };
    }
}
