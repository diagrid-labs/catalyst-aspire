using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Options;

public record CreatePubSubOptions
{
    public required string Project { get; init; }
    public IList<string> Scopes { get; init; } = [];
    public bool Wait { get; init; } = true;
}