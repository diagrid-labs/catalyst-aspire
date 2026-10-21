using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
[Collection(FakeDiagridCollection.Name)]
public class IdempotencyTests
{
    private static readonly ScenarioResponse AppGet =
        new() { ArgsMatch = "app get", StdOut = """{"status":{"apiToken":"diagrid://t"}}""" };

    // Exit 0 for everything.
    private static Scenario AlwaysSucceeds() => new() { Responses = [AppGet, new() { ExitCode = 0 }] };

    // Models a project whose resources all already exist: a create succeeds only when it carries
    // --ignore-if-exists, and otherwise fails the way the real CLI does on a conflict. This makes the flag
    // load-bearing: remove it from any builder and that create exits 1.
    private static Scenario EverythingAlreadyExists() => new()
    {
        Responses =
        [
            AppGet,
            new() { ArgsMatch = "--ignore-if-exists", ExitCode = 0 },
            new() { ArgsMatch = " create ", ExitCode = 1, StdErr = "Error: already exists" },
            new() { ExitCode = 0 },
        ],
    };

    private static async Task ProvisionAsync(bool includeSubscription = true)
    {
        var provisioner = new CliCatalystProvisioner();
        var ct = CancellationToken.None;

        await provisioner.CreateProject("demo", new(), ct);
        await provisioner.CreateApp("api", "demo", ct);
        var app = await provisioner.GetAppDetails("api", "demo", ct);
        Assert.Equal("diagrid://t", app.ApiToken);
        await provisioner.CreatePubSub("ps", new() { Project = "demo" }, ct);
        await provisioner.CreateKvStore("kv", new() { Project = "demo" }, ct);
        await provisioner.UpsertComponent(new()
        {
            Name = "store",
            Type = "state.in-memory",
            Metadata = new Dictionary<string, object?> { ["k"] = "v" },
        }, "demo", false, ct);

        if (includeSubscription)
        {
            await provisioner.UpsertSubscription("demo",
                new() { Name = "sub", Component = "ps", Topic = "t", Route = "/r" }, ct);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task Rerun_against_existing_resources_succeeds_for_every_create_that_has_ignore_if_exists()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(EverythingAlreadyExists());

        await ProvisionAsync(includeSubscription: false);
        await ProvisionAsync(includeSubscription: false);

        var creates = fake.Invocations.Where(i => i[1] == "create").ToList();
        Assert.Equal(10, creates.Count);
        Assert.All(creates, c => Assert.Contains("--ignore-if-exists", c));
        Assert.DoesNotContain(fake.Invocations, i => i[0] == "appid");
    }

    [Fact(Timeout = 60_000)]
    public async Task Rerun_without_ignore_if_exists_would_fail_proving_the_flag_is_load_bearing()
    {
        // Sanity check on the scenario itself: a bare create against an existing resource exits 1.
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(EverythingAlreadyExists());

        var result = await fake.RunAsync("project", "create", "demo");

        Assert.Equal(1, result.ExitCode);
    }

    // Task 12 closed the gap this test used to pin (it was Subscription_create_on_rerun_still_fails_loudly_until_task_12,
    // asserting that a rerun threw "already exists" from `subscription create`). subscription create has no
    // --ignore-if-exists, so a rerun now probes with `subscription get` and reconciles with `subscription update`.
    // Mutation: call `subscription create` unconditionally again -> this scenario answers it with exit 1 and the test throws.
    [Fact(Timeout = 60_000)]
    public async Task Subscription_on_rerun_is_reconciled_with_get_then_update_instead_of_failing()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(EverythingAlreadyExists());

        await ProvisionAsync(includeSubscription: true);

        var subscriptionCalls = fake.Invocations.Where(i => i[0] == "subscription").Select(i => i[1]).ToList();
        Assert.Equal(["get", "update"], subscriptionCalls);
    }

    [Fact(Timeout = 60_000)]
    public async Task Every_project_scoped_call_passes_project_explicitly_and_the_runner_appends_no_color()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(AlwaysSucceeds());

        await ProvisionAsync();

        // project create names the project positionally; no other provisioning call may rely on a current
        // project having been selected with `project use` (this driver never calls Init or UseProject).
        Assert.All(fake.Invocations.Where(i => i[0] != "project"), i => Assert.Contains("--project", i));
        Assert.All(fake.Invocations, i => Assert.Equal("--no-color", i[^1]));
    }

    [Fact(Timeout = 60_000)]
    public async Task Two_concurrent_provisioners_against_existing_resources_both_succeed()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(EverythingAlreadyExists());

        await Task.WhenAll(ProvisionAsync(includeSubscription: false), ProvisionAsync(includeSubscription: false));

        var creates = fake.Invocations.Where(i => i[1] == "create").ToList();
        Assert.Equal(10, creates.Count);
        Assert.All(creates, c => Assert.Contains("--ignore-if-exists", c));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failing_app_create_is_no_longer_swallowed()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ExitCode = 1, StdErr = "Error: boom" }] });

        await Assert.ThrowsAsync<CatalystCliException>(
            () => new CliCatalystProvisioner().CreateApp("api", "demo", default));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failing_subscription_create_is_no_longer_swallowed()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ExitCode = 1, StdErr = "Error: boom" }] });

        await Assert.ThrowsAsync<CatalystCliException>(
            () => new CliCatalystProvisioner().UpsertSubscription("demo",
                new() { Name = "sub", Component = "ps", Topic = "t", Route = "/r" }, default));
    }
}
