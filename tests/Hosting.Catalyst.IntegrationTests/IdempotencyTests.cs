using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
[Collection(FakeDiagridCollection.Name)]
public class IdempotencyTests
{
    // Exit 0 for everything, as --ignore-if-exists does when the resource already exists.
    private static Scenario AlwaysSucceeds() => new()
    {
        Responses =
        [
            new() { ArgsMatch = "app get", StdOut = """{"status":{"apiToken":"diagrid://t"}}""" },
            new() { ExitCode = 0 },
        ],
    };

    private static async Task ProvisionAsync()
    {
        var provisioner = new CliCatalystProvisioner();
        var ct = CancellationToken.None;

        await provisioner.CreateProject("demo", new(), ct);
        await provisioner.CreateApp("api", "demo", ct);
        var app = await provisioner.GetAppDetails("api", "demo", ct);
        Assert.Equal("diagrid://t", app.ApiToken);
        await provisioner.CreatePubSub("ps", new() { Project = "demo" }, ct);
        await provisioner.CreateKvStore("kv", new() { Project = "demo" }, ct);
        await provisioner.CreateComponent(new()
        {
            Name = "store",
            Type = "state.in-memory",
            Metadata = new Dictionary<string, object?> { ["k"] = "v" },
        }, "demo", ct);
        await provisioner.CreateSubscription("demo",
            new() { Name = "sub", Component = "ps", Topic = "t", Route = "/r" }, ct);
    }

    [Fact(Timeout = 60_000)]
    public async Task Second_provisioning_run_is_clean_and_passes_ignore_if_exists()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(AlwaysSucceeds());

        await ProvisionAsync();
        await ProvisionAsync();

        var creates = fake.Invocations.Where(i => i[1] == "create").ToList();
        Assert.Equal(12, creates.Count);
        Assert.All(creates.Where(c => c[0] != "subscription"), c => Assert.Contains("--ignore-if-exists", c));
        Assert.All(creates.Where(c => c[0] == "subscription"), c => Assert.DoesNotContain("--ignore-if-exists", c));
        Assert.DoesNotContain(fake.Invocations, i => i[0] == "appid");
    }

    [Fact(Timeout = 60_000)]
    public async Task Every_project_scoped_call_passes_project_and_none_depends_on_project_use()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(AlwaysSucceeds());

        await ProvisionAsync();

        // project create names the project positionally; no other call here may rely on a current project.
        Assert.All(fake.Invocations.Where(i => i[0] != "project"), i => Assert.Contains("--project", i));
        Assert.DoesNotContain(fake.Invocations, i => i is ["project", "use", ..]);
        // The runner appends --no-color as the trailing argument.
        Assert.All(fake.Invocations, i => Assert.Equal("--no-color", i[^1]));
    }

    [Fact(Timeout = 60_000)]
    public async Task Two_concurrent_provisioners_against_one_project_both_succeed()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(AlwaysSucceeds());

        await Task.WhenAll(ProvisionAsync(), ProvisionAsync());

        Assert.DoesNotContain(fake.Invocations, i => i is ["project", "use", ..]);
        Assert.Equal(12, fake.Invocations.Count(i => i[1] == "create"));
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
            () => new CliCatalystProvisioner().CreateSubscription("demo",
                new() { Name = "sub", Component = "ps", Topic = "t", Route = "/r" }, default));
    }
}
