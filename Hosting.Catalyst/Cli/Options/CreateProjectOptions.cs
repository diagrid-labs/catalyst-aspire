namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Options;

public record CreateProjectOptions
{
    public string? Region { get; init; }

    public bool DeployManagedPubsub { get; init; } = false;

    public bool DeployManagedKv { get; init; } = false;

    public bool EnableManagedWorkflow { get; init; } = false;

    public bool Wait { get; init; } = true;
    public bool Use { get; init; }
    public bool DisableAppTunnels { get; init; }
}