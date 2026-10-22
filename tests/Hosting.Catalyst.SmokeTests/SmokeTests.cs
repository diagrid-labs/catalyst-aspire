using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Test.AppHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hosting.Catalyst.SmokeTests;

public class SmokeTests
{
    // Only the shape matters: the model is built and provisioned, never run.
    private sealed class FakeApi : IProjectMetadata
    {
        public string ProjectPath => Path.Combine(AppContext.BaseDirectory, "TestApi.csproj");
        public LaunchSettings? LaunchSettings => null;
    }

    private sealed class HostLifetimeStub : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    // Every fact carries this guard; absent the variable the suite is skipped, so a developer without an
    // account still gets a green local run.
    [SkippableFact(Timeout = 25 * 60 * 1000)]
    public async Task Provisions_an_app_and_an_agent_with_state_and_pubsub_then_tears_down()
    {
        Skip.IfNot(SmokeProject.Enabled, "Set CATALYST_SMOKE_ENABLED=true (and log in) to run against a real Catalyst account.");

        var name = SmokeProject.NewName();
        try
        {
            // The USE_CATALYST=true AppHost wiring (test-api app, statestore, pubsub, subscription), plus an agent.
            var builder = DistributedApplication.CreateBuilder(["USE_CATALYST=true"]);
            DemoWiring.Configure<FakeApi>(builder, DemoWiring.UseCatalyst(builder), name);
            var catalyst = builder.Resources.OfType<CatalystProject>().Single();
            Assert.Equal(name, catalyst.ProjectName);
            builder.AddExecutable("my-agent", "dotnet", ".").WithCatalystAgent();

            var loggers = new ResourceLoggerService();
            var notifications = new ResourceNotificationService(
                NullLogger<ResourceNotificationService>.Instance, new HostLifetimeStub(), new EmptyServiceProvider(), loggers);
            var provisioner = new CliCatalystProvisioner();
            await new CatalystLifecycleHandler(catalyst, loggers, notifications, provisioner)
                .ProvisionCatalystAsync(CancellationToken.None);

            // The DAPR_* variables are built from exactly these values.
            Assert.False(string.IsNullOrWhiteSpace(await catalyst.HttpEndpoint.Task), "DAPR_HTTP_ENDPOINT would be empty");
            Assert.False(string.IsNullOrWhiteSpace(await catalyst.GrpcEndpoint.Task), "DAPR_GRPC_ENDPOINT would be empty");
            Assert.Equal(2, catalyst.Identities.Count);
            foreach (var (resource, identity) in catalyst.Identities)
            {
                var details = await identity.Details.Task;
                Assert.False(string.IsNullOrWhiteSpace(details.ApiToken), $"DAPR_API_TOKEN would be empty for {resource.Name}");
            }

            Assert.NotNull(await provisioner.TryGetProjectDetailsAsync(name, CancellationToken.None));
            Assert.True(await provisioner.KvStoreExistsAsync("statestore", name, CancellationToken.None), "statestore missing");
            Assert.True(await provisioner.PubSubExistsAsync("pubsub", name, CancellationToken.None), "pubsub missing");
        }
        finally
        {
            await SmokeProject.DeleteAsync(name);
        }
    }
}
