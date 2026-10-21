namespace Diagrid.Aspire.Hosting.Catalyst.Model;

public record ProjectDescriptor
{
    public bool EnableManagedWorkflow { get; init; }

    /// <summary>Region to create the project in (--region). Mutually exclusive with <see cref="RegionGroup"/>.</summary>
    public string? Region { get; init; }

    /// <summary>Region group to create the project in (--region-group). Mutually exclusive with <see cref="Region"/>.</summary>
    public string? RegionGroup { get; init; }

    /// <summary>Create-time only: deploy a managed key-value store with the project.</summary>
    public bool DeployManagedKv { get; init; }

    /// <summary>Create-time only: deploy a managed pub/sub with the project.</summary>
    public bool DeployManagedPubsub { get; init; }

    /// <summary>Project-level global id max body size (--global-id-max-body-size), e.g. "8Mi".</summary>
    public string? GlobalIdMaxBodySize { get; init; }

    /// <summary>Create-time only and irreversible; sent only when explicitly set.</summary>
    public bool? EnableWorkflowHistorySigning { get; init; }

    public bool DisableAppTunnels { get; init; }
}
