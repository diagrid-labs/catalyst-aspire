using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.ComponentSpec;

// see: https://docs.diagrid.io/references/components-reference/state/diagrid/

public class DiagridStateStore : CatalystComponent<DiagridStateSpecMetadata>
{
    public string Type => "state.diagrid";

    public IList<string> Scopes { get; init; } = [];

    public required DiagridStateSpecMetadata Metadata { get; init; }
}

public record DiagridStateSpecMetadata
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("keyPrefix")]
    public string? KeyPrefix { get; init; }

    [JsonPropertyName("outboxDiscardWhenMissingState")]
    public string? OutboxDiscardWhenMissingState { get; init; }

    [JsonPropertyName("outboxPublishPubsub")]
    public string? OutboxPublishPubsub { get; init; }

    [JsonPropertyName("outboxPublishTopic")]
    public string? OutboxPublishTopic { get; init; }

    [JsonPropertyName("outboxPubsub")]
    public string? OutboxPubsub { get; init; }
}
