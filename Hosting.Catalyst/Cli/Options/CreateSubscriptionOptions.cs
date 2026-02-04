using System;
using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Options;

public record CreateSubscriptionOptions
{
    public required string Project { get; init; }
    public required string Component { get; init; }
    public required string Topic { get; init; }
    public required string Route { get; init; }
    public IList<string> Scopes { get; init; } = new List<string>();
    public bool Bulk { get; init; }
    public int? BulkMaxMessagesCount { get; init; }
    public TimeSpan? BulkMaxAwaitDuration { get; init; }
    public bool Wait { get; init; } = true;
}
