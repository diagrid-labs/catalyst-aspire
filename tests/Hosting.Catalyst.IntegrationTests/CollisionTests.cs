using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
[Collection(FakeDiagridCollection.Name)]
public class CollisionTests
{
    private static ScenarioResponse ProjectGetReady()
        => new() { ArgsMatch = "project get", StdOut = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "project-get-ready.json")) };

    // `kv create --ignore-if-exists` exits 0 although a same-named component blocked it; the probe then finds no store.
    private static Scenario KvCreateOkButNoStore() => new()
    {
        Responses =
        [
            new() { ArgsMatch = "kv create", ExitCode = 0 },
            new() { ArgsMatch = "kv get", ExitCode = 1, StdErr = "Error: kv store not found\n" },
            new() { ExitCode = 0 },
        ],
    };

    private static Scenario PubSubCreateOkButNoBroker() => new()
    {
        Responses =
        [
            new() { ArgsMatch = "pubsub create", ExitCode = 0 },
            new() { ArgsMatch = "pubsub get", ExitCode = 1, StdErr = "Error: pubsub not found\n" },
            new() { ExitCode = 0 },
        ],
    };

    // --- through the provisioner ---

    // Mutation: drop the post-create probe from CliCatalystProvisioner.CreateKvStore and this no longer throws.
    [Fact(Timeout = 30_000)]
    public async Task Kv_create_that_succeeded_but_left_no_store_fails_naming_the_colliding_component()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(KvCreateOkButNoStore());

        var ex = await Assert.ThrowsAsync<CatalystCliException>(
            () => new CliCatalystProvisioner().CreateKvStore("statestore", new() { Project = "demo" }, default));

        Assert.Contains("statestore", ex.Message, StringComparison.Ordinal);
        Assert.Contains("component", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Rename", ex.Message, StringComparison.Ordinal);
        Assert.Contains(fake.Invocations, i => i is ["kv", "create", "statestore", ..]);
        Assert.Contains(fake.Invocations, i => i is ["kv", "get", "statestore", "--project", "demo", ..]);
    }

    [Fact(Timeout = 30_000)]
    public async Task Pubsub_create_that_succeeded_but_left_no_broker_fails_naming_the_colliding_component()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(PubSubCreateOkButNoBroker());

        var ex = await Assert.ThrowsAsync<CatalystCliException>(
            () => new CliCatalystProvisioner().CreatePubSub("pubsub", new() { Project = "demo" }, default));

        Assert.Contains("pubsub", ex.Message, StringComparison.Ordinal);
        Assert.Contains("component", ex.Message, StringComparison.Ordinal);
        Assert.Contains(fake.Invocations, i => i is ["pubsub", "get", "pubsub", "--project", "demo", ..]);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_store_that_exists_after_create_passes_the_check()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ExitCode = 0 }] });
        var provisioner = new CliCatalystProvisioner();

        await provisioner.CreateKvStore("statestore", new() { Project = "demo" }, default);
        await provisioner.CreatePubSub("pubsub", new() { Project = "demo" }, default);

        Assert.Contains(fake.Invocations, i => i is ["kv", "get", ..]);
        Assert.Contains(fake.Invocations, i => i is ["pubsub", "get", ..]);
    }

    // The probe reads the exit code only. Mutation: parse stdout, or treat any output as existence, and one of these flips.
    [Fact(Timeout = 30_000)]
    public async Task Existence_probes_follow_the_exit_code_alone()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses =
            [
                new() { ArgsMatch = "kv get present", ExitCode = 0, StdOut = "not json at all" },
                new() { ArgsMatch = "kv get absent", ExitCode = 1, StdOut = """{"metadata":{"name":"absent"}}""" },
                new() { ArgsMatch = "pubsub get present", ExitCode = 0 },
                new() { ArgsMatch = "pubsub get absent", ExitCode = 1 },
            ],
        });
        var provisioner = new CliCatalystProvisioner();

        Assert.True(await provisioner.KvStoreExists("present", "demo", default));
        Assert.False(await provisioner.KvStoreExists("absent", "demo", default));
        Assert.True(await provisioner.PubSubExists("present", "demo", default));
        Assert.False(await provisioner.PubSubExists("absent", "demo", default));
    }

    // Distinct names in a single run: a probe that checked the wrong noun or name would blame the wrong service.
    [Fact(Timeout = 30_000)]
    public async Task Probes_target_the_service_they_just_created()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses =
            [
                new() { ArgsMatch = "kv get kvname", ExitCode = 0 },
                new() { ArgsMatch = "pubsub get psname", ExitCode = 1 },
                new() { ExitCode = 0 },
            ],
        });
        var provisioner = new CliCatalystProvisioner();

        await provisioner.CreateKvStore("kvname", new() { Project = "demo" }, default);
        var ex = await Assert.ThrowsAsync<CatalystCliException>(
            () => provisioner.CreatePubSub("psname", new() { Project = "demo" }, default));

        Assert.Contains("psname", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("kvname", ex.Message, StringComparison.Ordinal);
    }

    // --- through the handler: the production path ---

    [Fact(Timeout = 60_000)]
    public async Task Provisioning_a_managed_service_creates_it_probes_it_and_never_issues_a_component_create()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [.. HandlerHarness.PreflightPasses(), ProjectGetReady(), new() { ExitCode = 0 }] });
        var (project, handler, _) = HandlerHarness.Build();
        project.KvStores.Add("statestore", new() { Project = "demo" });
        project.PubSubs.Add("pubsub", new() { Project = "demo" });

        await handler.ProvisionCatalyst(default);

        var kvCreate = fake.Invocations.ToList().FindIndex(i => i is ["kv", "create", "statestore", ..]);
        var kvGet = fake.Invocations.ToList().FindIndex(i => i is ["kv", "get", "statestore", ..]);
        var psCreate = fake.Invocations.ToList().FindIndex(i => i is ["pubsub", "create", "pubsub", ..]);
        var psGet = fake.Invocations.ToList().FindIndex(i => i is ["pubsub", "get", "pubsub", ..]);
        Assert.True(kvCreate >= 0 && kvGet > kvCreate, "kv get must follow kv create");
        Assert.True(psCreate >= 0 && psGet > psCreate, "pubsub get must follow pubsub create");
        Assert.DoesNotContain(fake.Invocations, i => i is ["component", ..]);
        Assert.DoesNotContain(fake.Invocations, i => i is ["kv", "list", ..]);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_collision_found_during_provisioning_fails_the_resource_and_stops_before_components()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses =
            [
                .. HandlerHarness.PreflightPasses(),
                ProjectGetReady(),
                new() { ArgsMatch = "kv get", ExitCode = 1, StdErr = "Error: not found\n" },
                new() { ExitCode = 0 },
            ],
        });
        var (project, handler, states) = HandlerHarness.Build();
        project.KvStores.Add("statestore", new() { Project = "demo" });
        project.Components.Add("later", new() { Name = "later", Type = "state.in-memory", Metadata = new Dictionary<string, object?> { ["k"] = "v" } });

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => handler.ProvisionCatalyst(default));
        await states.Drain();

        Assert.Contains("statestore", ex.Message, StringComparison.Ordinal);
        Assert.Contains("component", ex.Message, StringComparison.Ordinal);
        Assert.Contains(KnownResourceStates.FailedToStart, states.Snapshot());
        Assert.Contains(fake.Invocations, i => i is ["kv", "create", "statestore", ..]);
        Assert.DoesNotContain(fake.Invocations, i => i is ["component", ..]);
    }
}
