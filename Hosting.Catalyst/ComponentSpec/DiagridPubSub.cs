using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.ComponentSpec;

// see: https://docs.diagrid.io/references/components-reference/pubsub/diagrid/

public class DiagridPubSub : CatalystComponent<DiagridPubSubSpecMetadata>
{
    public string Type => "pubsub.diagrid";

    public IList<string> Scopes { get; init; } = [];

    public required DiagridPubSubSpecMetadata Metadata { get; init; }
}

public record DiagridPubSubSpecMetadata
{
    [JsonPropertyName("pubsub")]
    public required string PubSubName { get; init; }

    [JsonPropertyName("consumerID")]
    public string? ConsumerId { get; init; }
}
