using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
[Collection(FakeDiagridCollection.Name)]
public class ComponentReconcileTests
{
    private static ScenarioResponse ProjectGetReady()
        => new() { ArgsMatch = "project get", StdOut = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "project-get-ready.json")) };

    private static Scenario Everything() => new()
    {
        Responses = [.. HandlerHarness.PreflightPasses(), ProjectGetReady(), new() { ExitCode = 0 }],
    };

    private static ComponentDescriptor Component(string name) => new()
    {
        Name = name,
        Type = "state.postgresql",
        Metadata = new Dictionary<string, object?> { ["connectionString"] = "Host=x;Port=5432", ["actorStateStore"] = true },
    };

    private static int IndexOf(FakeDiagridFixture fake, Func<string[], bool> match)
        => fake.Invocations.ToList().FindIndex(i => match(i));

    // Mutation: have the handler always call create (or always update) -> one of the assertions fails.
    [Fact(Timeout = 60_000)]
    public async Task Updates_the_component_that_backs_a_declared_kv_store_after_the_store_is_created()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Everything());
        var (project, handler, _) = HandlerHarness.Build();
        project.KvStores.Add("statestore", new() { Project = "demo" });
        project.Components.Add("statestore", Component("statestore"));

        await handler.ProvisionCatalystAsync(default);

        var kvCreate = IndexOf(fake, i => i is ["kv", "create", "statestore", ..]);
        var update = IndexOf(fake, i => i is ["component", "update", "statestore", ..]);
        Assert.True(kvCreate >= 0, "the kv store must be created");
        Assert.True(update > kvCreate, "the component update must follow kv create");
        Assert.DoesNotContain(fake.Invocations, i => i is ["component", "create", ..]);

        var args = fake.Invocations[update];
        Assert.Contains("connectionString=Host=x;Port=5432", args);
        Assert.Contains("actorStateStore=true", args);
        Assert.Equal("demo", args[Array.IndexOf(args, "--project") + 1]);
    }

    // A managed KV store can already exist with NO component fronting it. Creating a project's first agent
    // provisions one named "kvstore" with CreateComponent=false (control plane: EnsureAgentRegistry ->
    // ensureDefaultKVStoreInfra -> CreateDefaultKVStore(WithCreateComponent(false))). `kv create` is then
    // refused as a conflict, --ignore-if-exists reports success, and the component is never created -- so
    // provisioning goes green and the app cannot address the store by name at runtime.
    // Mutation: drop the component create from EnsureServicesAsync and this fails.
    [Fact(Timeout = 60_000)]
    public async Task Fronts_a_declared_kv_store_with_a_component_when_the_model_declares_none()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Everything());
        var (project, handler, _) = HandlerHarness.Build();
        project.KvStores.Add("kvstore", new() { Project = "demo" });

        await handler.ProvisionCatalystAsync(default);

        var kvCreate = IndexOf(fake, i => i is ["kv", "create", "kvstore", ..]);
        var componentCreate = IndexOf(fake, i => i is ["component", "create", "kvstore", ..]);
        Assert.True(kvCreate >= 0, "the kv store must be created");
        Assert.True(componentCreate > kvCreate, "the fronting component must follow kv create");

        var args = fake.Invocations[componentCreate];
        Assert.Equal("state.diagrid", args[Array.IndexOf(args, "--type") + 1]);
        Assert.Contains("state=kvstore", args);
        Assert.Contains("--ignore-if-exists", args);
        Assert.Equal("demo", args[Array.IndexOf(args, "--project") + 1]);
    }

    [Fact(Timeout = 60_000)]
    public async Task Updates_the_component_that_backs_a_declared_pubsub()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Everything());
        var (project, handler, _) = HandlerHarness.Build();
        project.PubSubs.Add("pubsub", new() { Project = "demo" });
        project.Components.Add("pubsub", Component("pubsub"));

        await handler.ProvisionCatalystAsync(default);

        Assert.True(IndexOf(fake, i => i is ["pubsub", "create", "pubsub", ..]) >= 0);
        Assert.True(IndexOf(fake, i => i is ["component", "update", "pubsub", ..]) >= 0);
        Assert.DoesNotContain(fake.Invocations, i => i is ["component", "create", ..]);
    }

    [Fact(Timeout = 60_000)]
    public async Task Creates_a_component_that_backs_nothing_declared_and_never_updates_it()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Everything());
        var (project, handler, _) = HandlerHarness.Build();
        project.KvStores.Add("statestore", new() { Project = "demo" });
        project.Components.Add("diagrid-homepage", Component("diagrid-homepage"));

        await handler.ProvisionCatalystAsync(default);

        var args = fake.Invocations.Single(i => i is ["component", "create", "diagrid-homepage", ..]);
        Assert.Contains("--ignore-if-exists", args);
        Assert.DoesNotContain(fake.Invocations, i => i is ["component", "update", ..]);
    }

    [Fact(Timeout = 60_000)]
    public async Task Never_probes_with_component_get()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Everything());
        var (project, handler, _) = HandlerHarness.Build();
        project.KvStores.Add("statestore", new() { Project = "demo" });
        project.Components.Add("statestore", Component("statestore"));
        project.Components.Add("other", Component("other"));

        await handler.ProvisionCatalystAsync(default);

        Assert.True(IndexOf(fake, i => i is ["component", "update", ..]) >= 0);
        Assert.True(IndexOf(fake, i => i is ["component", "create", ..]) >= 0);
        Assert.DoesNotContain(fake.Invocations, i => i is ["component", "get", ..]);
    }
}
