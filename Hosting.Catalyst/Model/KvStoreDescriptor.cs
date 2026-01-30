using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst.Model;

public record KvStoreDescriptor
{
    public required string Project { get; init; }
    public IList<string> Scopes { get; init; } = [];
}