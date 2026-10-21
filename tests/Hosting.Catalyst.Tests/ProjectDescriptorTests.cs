using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Hosting.Catalyst.Tests;

public class ProjectDescriptorTests
{
    private static readonly string[] Defaults =
        ["--deploy-managed-pubsub=false", "--deploy-managed-kv=false", "--wait", "--ignore-if-exists"];

    [Fact]
    public void Passes_region_through_to_the_cli()
        => Assert.Equal(
            ["project", "create", "demo", "--region", "aws-us-west", .. Defaults],
            Commands.BuildCreateProjectArgs("demo", new() { Region = "aws-us-west" }));

    [Fact]
    public void Passes_region_group_through_to_the_cli()
        => Assert.Equal(
            ["project", "create", "demo", "--region-group", "prod-us", .. Defaults],
            Commands.BuildCreateProjectArgs("demo", new() { RegionGroup = "prod-us" }));

    [Fact]
    public void Uses_the_project_level_body_size_flag_name()
        => Assert.Equal(
            ["project", "create", "demo", "--global-id-max-body-size", "8Mi", .. Defaults],
            Commands.BuildCreateProjectArgs("demo", new() { GlobalIdMaxBodySize = "8Mi" }));

    [Fact]
    public void Rejects_region_and_region_group_together_before_invoking_the_cli()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Commands.BuildCreateProjectArgs("demo", new() { Region = "aws-us-west", RegionGroup = "prod-us" }));

        Assert.Contains("--region and --region-group are mutually exclusive", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sends_managed_service_flags_as_explicit_booleans()
        => Assert.Equal(
            ["project", "create", "demo", "--deploy-managed-pubsub=true", "--deploy-managed-kv=true",
             "--wait", "--ignore-if-exists"],
            Commands.BuildCreateProjectArgs("demo", new() { DeployManagedKv = true, DeployManagedPubsub = true }));

    [Theory]
    [InlineData(true, "--enable-workflow-history-signing=true")]
    [InlineData(false, "--enable-workflow-history-signing=false")]
    public void Sends_history_signing_when_explicitly_set(bool value, string expected)
        => Assert.Equal(
            ["project", "create", "demo", expected, .. Defaults],
            Commands.BuildCreateProjectArgs("demo", new() { EnableWorkflowHistorySigning = value }));

    [Fact]
    public void Omits_history_signing_when_unset()
        => Assert.Equal(
            ["project", "create", "demo", .. Defaults],
            Commands.BuildCreateProjectArgs("demo", new()));

    [Fact]
    public void Passes_disable_app_tunnels_when_set()
        => Assert.Equal(
            ["project", "create", "demo", "--deploy-managed-pubsub=false", "--deploy-managed-kv=false",
             "--wait", "--disable-app-tunnels", "--ignore-if-exists"],
            Commands.BuildCreateProjectArgs("demo", new() { DisableAppTunnels = true }));

    [Fact]
    public void Provisioner_maps_every_descriptor_member_to_the_cli_options()
    {
        var options = CliCatalystProvisioner.ToCreateOptions(new ProjectDescriptor
        {
            EnableManagedWorkflow = true,
            Region = "aws-us-west",
            DeployManagedKv = true,
            DeployManagedPubsub = true,
            GlobalIdMaxBodySize = "8Mi",
            EnableWorkflowHistorySigning = true,
            DisableAppTunnels = true,
        });

        Assert.Equal(
            ["project", "create", "demo", "--region", "aws-us-west", "--global-id-max-body-size", "8Mi",
             "--enable-workflow-history-signing=true", "--deploy-managed-pubsub=true", "--deploy-managed-kv=true",
             "--enable-managed-workflow", "--wait", "--disable-app-tunnels", "--ignore-if-exists"],
            Commands.BuildCreateProjectArgs("demo", options));
        Assert.Equal("prod-us", CliCatalystProvisioner.ToCreateOptions(new ProjectDescriptor { RegionGroup = "prod-us" }).RegionGroup);
    }

    [Fact]
    public void Descriptor_defaults_preserve_the_previous_behaviour()
    {
        var d = new ProjectDescriptor { EnableManagedWorkflow = true };

        Assert.Null(d.Region);
        Assert.Null(d.RegionGroup);
        Assert.Null(d.GlobalIdMaxBodySize);
        Assert.Null(d.EnableWorkflowHistorySigning);
        Assert.False(d.DeployManagedKv);
        Assert.False(d.DeployManagedPubsub);
        Assert.False(d.DisableAppTunnels);
    }
}
