using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst.Model;

public record SubscriptionDescriptor
{
    public required string Name { get; init; }
    public required string Component { get; init; }
    public required string Topic { get; init; }
    public required string Route { get; init; }
    public IList<string> Scopes { get; init; } = new List<string>();
}
