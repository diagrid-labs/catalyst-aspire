namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Options;

public record CreateProjectOptions
{
    public string? Region { get; init; }

    public string? RegionGroup { get; init; }

    public string? GlobalIdMaxBodySize { get; init; }

    public bool? EnableWorkflowHistorySigning { get; init; }

    public bool DeployManagedPubsub { get; init; }

    public bool DeployManagedKv { get; init; }

    public bool EnableManagedWorkflow { get; init; }

    public bool Wait { get; init; } = true;
    public bool IgnoreIfExists { get; init; } = true;
    public bool Use { get; init; }
    public bool DisableAppTunnels { get; init; }
}
