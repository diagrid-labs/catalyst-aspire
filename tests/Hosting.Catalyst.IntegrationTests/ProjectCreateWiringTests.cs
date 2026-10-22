using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
[Collection(FakeDiagridCollection.Name)]
public class ProjectCreateWiringTests
{
    // Pins the wiring, not the mapping: reverting CliCatalystProvisioner.CreateProjectAsync to hardcoded
    // managed-service flags and no region makes this fail.
    [Fact(Timeout = 30_000)]
    public async Task Provisioner_CreateProject_sends_the_descriptor_options_to_the_cli()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ExitCode = 0 }] });

        await new CliCatalystProvisioner().CreateProjectAsync("demo", new ProjectDescriptor
        {
            EnableManagedWorkflow = true,
            Region = "aws-us-west",
            DeployManagedKv = true,
            DeployManagedPubsub = false,
            GlobalIdMaxBodySize = "8Mi",
            EnableWorkflowHistorySigning = true,
            DisableAppTunnels = true,
        }, CancellationToken.None);

        Assert.Equal(
            ["project", "create", "demo", "--region", "aws-us-west", "--global-id-max-body-size", "8Mi",
             "--enable-workflow-history-signing=true", "--deploy-managed-pubsub=false", "--deploy-managed-kv=true",
             "--enable-managed-workflow", "--wait", "--disable-app-tunnels", "--ignore-if-exists", "--no-color=true"],
            fake.Invocations.Single());
    }
}
