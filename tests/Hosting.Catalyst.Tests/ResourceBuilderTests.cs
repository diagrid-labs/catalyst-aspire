using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Hosting.Catalyst.Tests;

public class ResourceBuilderTests
{
    [Fact]
    public void WithCatalyst_and_WithCatalystApp_produce_identical_resource_graphs()
    {
        var viaAlias = GraphOf(b => b.WithCatalyst());
        var viaApp = GraphOf(b => b.WithCatalystApp());

        Assert.NotEmpty(viaApp);
        Assert.Equal(viaAlias, viaApp);
        // Guards against a vacuous comparison: the graph must include the proxy and its arguments.
        Assert.Contains(viaApp, line => line.StartsWith("worker-catalyst-proxy args:", StringComparison.Ordinal));
    }

    [Fact]
    public void Registers_the_identity_for_a_resource_with_no_http_endpoint()
    {
        var (project, worker) = BuildWorkerWithNoEndpoint();

        var identity = Assert.Single(project.Identities);
        Assert.Same(worker.Resource, identity.Key);
        Assert.Equal(IdentityKind.App, identity.Value.Kind);
    }

    [Fact]
    public void Omits_app_port_from_dev_run_for_a_resource_with_no_http_endpoint()
    {
        var (project, _) = BuildWorkerWithNoEndpoint();
        var args = DevRunArgsFor(project);

        Assert.DoesNotContain("--app-port", args);
        Assert.Contains("--id", args);
        Assert.DoesNotContain("--app-id", args);
    }

    [Fact]
    public void Passes_app_port_to_dev_run_for_a_resource_with_an_http_endpoint()
    {
        var builder = DistributedApplication.CreateBuilder();
        var catalyst = builder.AddCatalystProject("demo");
        builder.AddContainer("api", "img").WithHttpEndpoint(targetPort: 8080).WithCatalystApp();

        var args = DevRunArgsFor(catalyst.Resource, "api");

        var index = args.IndexOf("--app-port");
        Assert.True(index >= 0, "--app-port should be present when the resource has an HTTP endpoint");
        Assert.Equal("{api.bindings.http.port}", args[index + 1]);
        Assert.Equal("api", args[args.IndexOf("--id") + 1]);
    }

    [Fact]
    public void Rejects_an_invalid_resource_name_at_registration_naming_the_resource()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddCatalystProject("demo");
        var bad = builder.AddExecutable("TestApi", "dotnet", ".");

        var ex = Assert.Throws<ArgumentException>(() => bad.WithCatalystApp());
        Assert.Contains("TestApi", ex.Message, StringComparison.Ordinal);
    }

    // Aspire accepts resource names up to 64 characters (measured: 64 ok, 65 rejected with ASPIRE006).
    // The proxy is named "{name}-catalyst-proxy", 15 characters longer, so the longest usable name is 49.
    [Fact]
    public void Accepts_a_resource_name_whose_proxy_name_is_exactly_at_the_aspire_limit()
    {
        var builder = DistributedApplication.CreateBuilder();
        var catalyst = builder.AddCatalystProject("demo");
        var name = new string('a', 49);

        builder.AddExecutable(name, "dotnet", ".").WithCatalystApp();

        Assert.Single(catalyst.Resource.Identities);
        Assert.Equal(64, $"{name}-catalyst-proxy".Length);
    }

    [Fact]
    public void Rejects_a_resource_name_whose_proxy_name_exceeds_the_aspire_limit_naming_the_resource()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddCatalystProject("demo");
        var name = new string('a', 50);
        var resource = builder.AddExecutable(name, "dotnet", ".");

        var ex = Assert.Throws<ArgumentException>(() => resource.WithCatalystApp());

        Assert.Contains($"'{name}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("too long", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Environment_callback_resolves_for_a_resource_with_no_http_endpoint()
    {
        var builder = DistributedApplication.CreateBuilder();
        var catalyst = builder.AddCatalystProject("demo");
        var worker = builder.AddExecutable("worker", "dotnet", ".").WithCatalystApp();

        var project = catalyst.Resource;
        project.GrpcEndpoint.SetResult("https://grpc.example");
        project.HttpEndpoint.SetResult("https://http.example");
        project.Identities[worker.Resource].Details.SetResult(new AppDetails { ApiToken = "tok" });

        var services = new ServiceCollection()
            .AddSingleton(new DistributedApplicationModel(builder.Resources))
            .BuildServiceProvider();
        var executionContext = new DistributedApplicationExecutionContext(
            new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run) { ServiceProvider = services });
        var env = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(executionContext, env);

        foreach (var annotation in worker.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await annotation.Callback(context);

        Assert.Equal("https://grpc.example", env["DAPR_GRPC_ENDPOINT"]);
        Assert.Equal("https://http.example", env["DAPR_HTTP_ENDPOINT"]);
        Assert.Equal("tok", env["DAPR_API_TOKEN"]);
    }

    private static (CatalystProject Project, IResourceBuilder<ExecutableResource> Worker) BuildWorkerWithNoEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();
        var catalyst = builder.AddCatalystProject("demo");
        var worker = builder.AddExecutable("worker", "dotnet", ".").WithCatalystApp();
        return (catalyst.Resource, worker);
    }

    private static List<string> DevRunArgsFor(CatalystProject project, string resourceName = "worker")
    {
        return ArgsOf(ProxyOf(project, resourceName));
    }

    private static ExecutableResource ProxyOf(CatalystProject project, string resourceName)
    {
        var owner = project.Identities.Keys.Single(r => r.Name == resourceName);
        var proxy = owner.Annotations.OfType<ResourceRelationshipAnnotation>()
            .Select(a => a.Resource)
            .OfType<ExecutableResource>()
            .Single(r => r.Name == $"{resourceName}-catalyst-proxy");
        return proxy;
    }

    private static List<string> ArgsOf(ExecutableResource proxy)
    {
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args, proxy);
        foreach (var annotation in proxy.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            annotation.Callback(context).GetAwaiter().GetResult();

        return args
            .Select(a => a is IManifestExpressionProvider m ? m.ValueExpression : a.ToString()!)
            .ToList();
    }

    private static List<string> GraphOf(Action<IResourceBuilder<ExecutableResource>> configure)
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddCatalystProject("demo");
        var worker = builder.AddExecutable("worker", "dotnet", ".");
        configure(worker);

        var lines = new List<string>();
        foreach (var resource in builder.Resources.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            lines.Add($"{resource.Name} [{string.Join(",", resource.Annotations.Select(a => a.GetType().Name).OrderBy(n => n, StringComparer.Ordinal))}]");
            if (resource is ExecutableResource exe && exe.Name.EndsWith("-catalyst-proxy", StringComparison.Ordinal))
                lines.Add($"{exe.Name} args: {string.Join(" ", ArgsOf(exe))}");
            if (resource is CatalystProject p)
                lines.Add($"identities: {string.Join(",", p.Identities.Select(i => $"{i.Key.Name}:{i.Value.Kind}"))}");
        }
        return lines;
    }
}
