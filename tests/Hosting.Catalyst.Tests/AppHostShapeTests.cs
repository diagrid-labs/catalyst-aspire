using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Test.AppHost;

namespace Hosting.Catalyst.Tests;

// These tests build the distributed application model and never run it: no DCP, no process, no diagrid CLI.
public class AppHostShapeTests
{
    private sealed class FakeApi : IProjectMetadata
    {
        public string ProjectPath => RepoFile("TestApi", "TestApi.csproj");
        public LaunchSettings? LaunchSettings => null;
    }

    private static (IDistributedApplicationBuilder Builder, IResourceBuilder<ProjectResource> Api) Build(bool useCatalyst)
    {
        var builder = DistributedApplication.CreateBuilder();
        return (builder, DemoWiring.Configure<FakeApi>(builder, useCatalyst));
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "catalyst-aspire.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. parts]);
    }

    private static string ComponentNameIn(string yamlPath)
    {
        var match = Regex.Match(File.ReadAllText(yamlPath), @"(?m)^metadata:\s*\r?\n\s+name:\s*(\S+)", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"no metadata.name in {yamlPath}");
        return match.Groups[1].Value;
    }

    private static string OssSidecarResourcesDir(IResourceBuilder<ProjectResource> api)
    {
        var sidecar = Assert.Single(api.Resource.Annotations.OfType<DaprSidecarAnnotation>());
        var options = Assert.Single(((IResource)sidecar.Sidecar).Annotations.OfType<DaprSidecarOptionsAnnotation>());
        return Assert.Single(options.Options!.ResourcesPaths);
    }

    // Mutation: drop the if/else (always wire one side) -> one of the two rows fails.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppHost_builds_under_both_switch_positions(bool useCatalyst)
    {
        var (builder, api) = Build(useCatalyst);

        Assert.Contains(builder.Resources, r => r.Name == "test-api");
        Assert.Equal(useCatalyst, builder.Resources.Any(r => r is CatalystProject));
        Assert.Equal(useCatalyst, !api.Resource.Annotations.OfType<DaprSidecarAnnotation>().Any());
    }

    // Mutation: ignore USE_CATALYST in UseCatalyst() -> the true row sees no Catalyst project.
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void The_USE_CATALYST_setting_selects_the_side(string value, bool expectCatalyst)
    {
        var builder = DistributedApplication.CreateBuilder([$"USE_CATALYST={value}"]);
        DemoWiring.Configure<FakeApi>(builder, DemoWiring.UseCatalyst(builder));

        Assert.Equal(expectCatalyst, builder.Resources.Any(r => r is CatalystProject));
    }

    // The failure this whole effort exists to prevent: each side wired to a different component name. Compared
    // per kind (state vs pubsub), so a transposition cannot pass.
    // Mutation: rename either YAML component, or give WithCatalystKvStore/WithCatalystPubSub another name.
    [Fact]
    public void Both_sides_name_the_same_state_and_pubsub_components()
    {
        var (catalystBuilder, _) = Build(true);
        var catalyst = catalystBuilder.Resources.OfType<CatalystProject>().Single();
        var (_, ossApi) = Build(false);
        var resources = OssSidecarResourcesDir(ossApi);

        var ossState = ComponentNameIn(Path.Combine(resources, "statestore.yaml"));
        var ossPubSub = ComponentNameIn(Path.Combine(resources, "pubsub.yaml"));

        Assert.Equal("statestore", ossState);
        Assert.Equal("pubsub", ossPubSub);
        Assert.Equal([ossState], catalyst.KvStores.Keys);
        Assert.Equal([ossPubSub], catalyst.PubSubs.Keys);
        Assert.Equal(ossPubSub, Assert.Single(catalyst.Subscriptions).Value.Component);
    }

    // Each YAML file must declare the right component type for its name (a state store named "statestore").
    // Mutation: swap the types in the two files.
    [Fact]
    public void The_oss_yaml_declares_a_state_store_and_a_pubsub()
    {
        var (_, api) = Build(false);
        var resources = OssSidecarResourcesDir(api);

        Assert.Contains("type: state.", File.ReadAllText(Path.Combine(resources, "statestore.yaml")), StringComparison.Ordinal);
        Assert.Contains("type: pubsub.", File.ReadAllText(Path.Combine(resources, "pubsub.yaml")), StringComparison.Ordinal);
    }

    // Mutation: change the AppHost subscription route, or the TestApi route, independently.
    [Fact]
    public void TestApi_uses_the_component_names_and_serves_the_subscription_route()
    {
        var source = File.ReadAllText(RepoFile("TestApi", "Program.cs"));
        var (catalystBuilder, _) = Build(true);
        var subscription = Assert.Single(catalystBuilder.Resources.OfType<CatalystProject>().Single().Subscriptions).Value;

        Assert.Contains("const string StateStore = \"statestore\";", source, StringComparison.Ordinal);
        Assert.Contains("const string PubSub = \"pubsub\";", source, StringComparison.Ordinal);
        Assert.Contains($"MapPost(\"{subscription.Route}\"", source, StringComparison.Ordinal);
        Assert.Equal("orders", subscription.Topic);
        Assert.Contains("const string Topic = \"orders\";", source, StringComparison.Ordinal);
    }

    // Workflows are durable through the managed workflows-state component; a second flagged store would make
    // which one backs workflows nondeterministic. The KV descriptor has no actor flag, so assert that no extra
    // user component was registered that could carry one.
    // Mutation: add a WithComponent state store to the Catalyst branch, or drop EnableManagedWorkflow.
    [Fact]
    public void Workflows_rely_on_the_managed_store_alone()
    {
        var (builder, _) = Build(true);
        var catalyst = builder.Resources.OfType<CatalystProject>().Single();

        Assert.True(catalyst.ProjectDescriptor.EnableManagedWorkflow);
        Assert.Empty(catalyst.Components);
    }
}
