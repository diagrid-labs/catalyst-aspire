using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst.Model;

public record ComponentDescriptor
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public IList<string> Scopes { get; init; } = [];
    public required IDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}