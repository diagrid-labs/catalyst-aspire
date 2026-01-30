namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Options;

public record CreateAppOptions
{
    public string? Project { get; init; }
    public string? AppEndpoint { get; init; }
    public string? AppToken { get; init; }
    public string? AppProtocol { get; init; }
    public string? AppConfig { get; init; }
    public bool EnableAppHealthCheck { get; init; }
    public string? AppHealthCheckPath { get; init; }
    public int? AppHealthProbeInterval { get; init; }
    public int? AppHealthProbeTimeout { get; init; }
    public int? AppHealthThreshold { get; init; }
    public int? AppChannelTimeoutSeconds { get; init; }

    public bool Wait { get; init; } = true;
}