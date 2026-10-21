using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
// These drive CatalystLifecycleHandler.ProvisionCatalyst over subscriptions declared through the real fluent API,
// so they pin that production calls the reconcile (not just that a helper works).
[Collection(FakeDiagridCollection.Name)]
public class SubscriptionReconcileTests
{
    private static ScenarioResponse ProjectGetReady()
        => new() { ArgsMatch = "project get", StdOut = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "project-get-ready.json")) };

    private static Scenario Scenario(IReadOnlyList<ScenarioResponse>? before = null)
        => new() { Responses = [.. before ?? [], .. HandlerHarness.PreflightPasses(), ProjectGetReady(), new() { ExitCode = 0 }] };

    /// <summary>Declares subscriptions through the public API on a real Aspire graph, then copies them onto the handler's project.</summary>
    private static Action<CatalystProject> Declared(Action<IResourceBuilder<ExecutableResource>> declare) => project =>
    {
        var builder = DistributedApplication.CreateBuilder();
        var catalyst = builder.AddCatalystProject("demo");
        declare(builder.AddExecutable("test-api", "dotnet", ".").WithCatalystApp());

        foreach (var pair in catalyst.Resource.Subscriptions)
        {
            project.Subscriptions.Add(pair.Key, pair.Value);
        }
    };

    private static string[] Single(FakeDiagridFixture fake, Func<string[], bool> match)
        => fake.Invocations.Single(match);

    private static string ValueAfter(string[] args, string flag) => args[Array.IndexOf(args, flag) + 1];

    // Mutation: always create -> 'update' is never seen; always update -> 'create' is never seen.
    [Fact(Timeout = 60_000)]
    public async Task An_existing_subscription_is_probed_then_updated_never_created()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario());
        var (_, handler, _, _) = HandlerHarness.BuildWithLogs(Declared(b => b.WithSubscription("orders", "/orders")));

        await handler.ProvisionCatalyst(default);

        var probe = fake.Invocations.ToList().FindIndex(i => i is ["subscription", "get", "test-api-pubsub-orders", ..]);
        var update = fake.Invocations.ToList().FindIndex(i => i is ["subscription", "update", "test-api-pubsub-orders", ..]);
        Assert.True(probe >= 0, "the subscription must be probed with 'subscription get'");
        Assert.True(update > probe, "the update must follow the probe");
        Assert.DoesNotContain(fake.Invocations, i => i is ["subscription", "create", ..]);
        Assert.Equal("demo", ValueAfter(fake.Invocations[probe], "--project"));
    }

    [Fact(Timeout = 60_000)]
    public async Task An_absent_subscription_is_probed_then_created_never_updated()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario([new() { ArgsMatch = "subscription get", ExitCode = 1, StdErr = "Error: not found" }]));
        var (_, handler, _, _) = HandlerHarness.BuildWithLogs(Declared(b => b.WithSubscription("orders", "/orders")));

        await handler.ProvisionCatalyst(default);

        var probe = fake.Invocations.ToList().FindIndex(i => i is ["subscription", "get", ..]);
        var create = fake.Invocations.ToList().FindIndex(i => i is ["subscription", "create", "test-api-pubsub-orders", ..]);
        Assert.True(probe >= 0);
        Assert.True(create > probe, "the create must follow the probe");
        Assert.DoesNotContain(fake.Invocations, i => i is ["subscription", "update", ..]);
        Assert.DoesNotContain("--ignore-if-exists", fake.Invocations[create]);
    }

    // The scenario exists for one name and not the other, and each name goes to its own branch: swapping the names,
    // or reusing one probe result for both, sends the wrong verb to the wrong subscription.
    [Fact(Timeout = 60_000)]
    public async Task Two_topics_on_one_app_are_each_reconciled_independently()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario([
            new() { ArgsMatch = "subscription get test-api-pubsub-shipments", ExitCode = 1 },
            new() { ArgsMatch = "subscription get test-api-pubsub-orders", ExitCode = 0 }]));
        var (_, handler, _, _) = HandlerHarness.BuildWithLogs(
            Declared(b => b.WithSubscription("orders", "/orders-route").WithSubscription("shipments", "/shipments-route")));

        await handler.ProvisionCatalyst(default);

        var update = Single(fake, i => i is ["subscription", "update", ..]);
        var create = Single(fake, i => i is ["subscription", "create", ..]);
        Assert.Equal("test-api-pubsub-orders", update[2]);
        Assert.Equal("orders", ValueAfter(update, "--topic"));
        Assert.Equal("/orders-route", ValueAfter(update, "--route"));
        Assert.Equal("test-api-pubsub-shipments", create[2]);
        Assert.Equal("shipments", ValueAfter(create, "--topic"));
        Assert.Equal("/shipments-route", ValueAfter(create, "--route"));
    }

    [Fact(Timeout = 60_000)]
    public async Task Update_sends_every_declared_value_including_bulk_options_as_go_durations()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario());
        var (_, handler, _, _) = HandlerHarness.BuildWithLogs(Declared(b =>
            b.WithSubscription("sub-1", "events", "orders", "/o", true, 25, TimeSpan.FromSeconds(90))));

        await handler.ProvisionCatalyst(default);

        var update = Single(fake, i => i is ["subscription", "update", "sub-1", ..]);
        Assert.Equal("events", ValueAfter(update, "--component"));
        Assert.Equal("orders", ValueAfter(update, "--topic"));
        Assert.Equal("/o", ValueAfter(update, "--route"));
        Assert.Equal("test-api", ValueAfter(update, "--scopes"));
        Assert.Contains("--bulk=true", update);
        Assert.Equal("25", ValueAfter(update, "--bulk-max-messages-count"));
        Assert.Equal("1m30s", ValueAfter(update, "--bulk-max-await-duration"));
        Assert.Equal("demo", ValueAfter(update, "--project"));
    }

    [Fact(Timeout = 60_000)]
    public async Task Create_sends_the_bulk_options_too()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario([new() { ArgsMatch = "subscription get", ExitCode = 1 }]));
        var (_, handler, _, _) = HandlerHarness.BuildWithLogs(Declared(b =>
            b.WithSubscription("sub-1", "events", "orders", "/o", true, 25, TimeSpan.FromSeconds(2))));

        await handler.ProvisionCatalyst(default);

        var create = Single(fake, i => i is ["subscription", "create", "sub-1", ..]);
        Assert.Contains("--bulk", create);
        Assert.Equal("25", ValueAfter(create, "--bulk-max-messages-count"));
        Assert.Equal("2s", ValueAfter(create, "--bulk-max-await-duration"));
    }

    // The probe reads only an exit code, so an auth or network failure looks like "absent". The create that follows
    // must then fail loudly rather than the handler carrying on.
    [Fact(Timeout = 60_000)]
    public async Task A_failing_probe_that_is_really_an_outage_surfaces_as_a_failed_create()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario([
            new() { ArgsMatch = "subscription get", ExitCode = 1, StdErr = "Error: network unreachable" },
            new() { ArgsMatch = "subscription create", ExitCode = 1, StdErr = "Error: network unreachable" }]));
        var (_, handler, states, _) = HandlerHarness.BuildWithLogs(Declared(b => b.WithSubscription("orders", "/orders")));

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => handler.ProvisionCatalyst(default));

        Assert.Equal(["subscription", "create"], ex.Arguments.Take(2));
        await states.Drain();
        Assert.Contains(KnownResourceStates.FailedToStart, states.Snapshot());
    }

    [Fact(Timeout = 60_000)]
    public async Task A_failing_update_surfaces_and_marks_the_project_failed()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario([new() { ArgsMatch = "subscription update", ExitCode = 1, StdErr = "Error: boom" }]));
        var (_, handler, states, _) = HandlerHarness.BuildWithLogs(Declared(b => b.WithSubscription("orders", "/orders")));

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => handler.ProvisionCatalyst(default));

        Assert.Equal(["subscription", "update"], ex.Arguments.Take(2));
        await states.Drain();
        Assert.Contains(KnownResourceStates.FailedToStart, states.Snapshot());
    }
}
