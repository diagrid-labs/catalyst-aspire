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
    // Only the shape matters: the model is built and provisioned, never run. The path must still resolve to a
    // real .csproj, because Aspire reads the project file while building the model -- AppContext.BaseDirectory
    // is this test's bin directory, which never contains one.
    private sealed class FakeApi : IProjectMetadata
    {
        public string ProjectPath => RepoFile("TestApi", "TestApi.csproj");
        public LaunchSettings? LaunchSettings => null;
    }

    /// <summary>Resolves a path from the repository root, found by walking up to the solution file.</summary>
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "catalyst-aspire.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. parts]);
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

    // Deliberately NOT skippable. Every other test here needs an account, so the harness itself was only ever
    // exercised by the nightly -- and the first nightly that ran it failed before reaching Catalyst at all,
    // because ProjectPath pointed inside this test's own bin directory where no .csproj can exist.
    // Aspire reads the project file while building the model, so a wrong path breaks the smoke test for a
    // reason that has nothing to do with Catalyst. This check costs nothing and runs on every CI build.
    [Fact]
    public void The_harness_points_at_the_real_TestApi_project()
    {
        var path = new FakeApi().ProjectPath;

        Assert.True(File.Exists(path), $"FakeApi.ProjectPath does not exist: {path}");
        Assert.Equal("TestApi.csproj", Path.GetFileName(path));
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
            // The USE_CATALYST=true AppHost wiring (test-api app, kvstore, pubsub, subscription), plus an agent.
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
            Assert.True(await provisioner.KvStoreExistsAsync("kvstore", name, CancellationToken.None), "kvstore missing");
            Assert.True(await provisioner.PubSubExistsAsync("pubsub", name, CancellationToken.None), "pubsub missing");
        }
        finally
        {
            await SmokeProject.DeleteAsync(name);
        }
    }
}
