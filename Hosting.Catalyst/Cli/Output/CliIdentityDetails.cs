using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

/// <summary>
/// One model for the JSON of <c>app get</c>, <c>agent get</c> and the legacy <c>appid get</c>.
/// Nothing is <c>required</c>: the CLI omits fields freely and a missing one reads as absent.
/// </summary>
public record CliIdentityDetails
{
    [JsonPropertyName("status")]
    public CliIdentityStatus? Status { get; init; }

    /// <summary>
    /// Returns <c>status.apiToken</c> when non-empty (App, App ID), else
    /// <c>status.appIds[0].status.apiToken</c> (Agent, which has no top-level token), else throws.
    /// </summary>
    public string ResolveApiToken(string resourceName, IdentityKind kind, string context)
    {
        var token = Status?.ApiToken;
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        token = Status?.AppIds?.FirstOrDefault()?.Status?.ApiToken;
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        var message =
            $"Could not find an API token for '{resourceName}' ({context}). " +
            "Neither status.apiToken nor status.appIds[0].status.apiToken was present in the CLI output.";
        if (kind == IdentityKind.Agent)
        {
            message += " The Agent API may be unavailable on this deployment.";
        }

        throw new InvalidOperationException(message);
    }
}

public record CliIdentityStatus
{
    [JsonPropertyName("apiToken")]
    public string? ApiToken { get; init; }

    [JsonPropertyName("appIds")]
    public IList<CliEmbeddedAppId>? AppIds { get; init; }
}

public record CliEmbeddedAppId
{
    [JsonPropertyName("status")]
    public CliEmbeddedAppIdStatus? Status { get; init; }
}

public record CliEmbeddedAppIdStatus
{
    [JsonPropertyName("apiToken")]
    public string? ApiToken { get; init; }
}
