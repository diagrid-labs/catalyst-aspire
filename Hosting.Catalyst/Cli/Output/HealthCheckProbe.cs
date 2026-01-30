using System.Text.Json.Serialization;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

public record HealthCheckProbe
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("failureThreshold")]
    public int FailureThreshold { get; init; }

    [JsonPropertyName("intervalInSec")]
    public int IntervalInSec { get; init; }

    [JsonPropertyName("timeoutInMs")]
    public int TimeoutInMs { get; init; }
}