using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
// These drive CatalystLifecycleHandler.ProvisionCatalystAsync over identities declared through the real fluent API,
// so they pin that production dispatches on IdentityKind (not just that a helper works).
[Collection(FakeDiagridCollection.Name)]
public class AgentProvisioningTests
{
    private const string AgentName = "my-agent";
    private const string AppName = "my-app";
    // agent-get.json carries this ONLY at status.appIds[0].status.apiToken; there is no status.apiToken.
    private const string AgentToken = "diagrid://mock-api-token";
    private const string AppToken = "diagrid://the-app-token";

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    private static ScenarioResponse ProjectGetReady() => new() { ArgsMatch = "project get", StdOut = Fixture("project-get-ready.json") };

    private static Scenario Scenario(string agentGet, params ScenarioResponse[] before) => new()
    {
        Responses =
        [
            .. before,
            .. HandlerHarness.PreflightPasses(),
            ProjectGetReady(),
            new() { ArgsMatch = "agent get", StdOut = agentGet },
            new() { ArgsMatch = "app get", StdOut = "{\"status\":{\"apiToken\":\"" + AppToken + "\"}}" },
            new() { ExitCode = 0 },
        ],
    };

    /// <summary>Declares the resources through the public API on a real Aspire graph, then copies the identities onto the handler's project.</summary>
    private static Action<CatalystProject> Declared(bool agent = true, bool app = false) => project =>
    {
        var builder = DistributedApplication.CreateBuilder();
        var catalyst = builder.AddCatalystProject("demo");
        if (agent)
        {
            builder.AddExecutable(AgentName, "dotnet", ".").WithCatalystAgent();
        }

        if (app)
        {
            builder.AddExecutable(AppName, "dotnet", ".").WithCatalystApp();
        }

        foreach (var pair in catalyst.Resource.Identities)
        {
            project.Identities.Add(pair.Key, pair.Value);
        }
    };

    private static string TokenOf(CatalystProject project, string name)
        => project.Identities.Single(i => i.Key.Name == name).Value.Details.Task.GetAwaiter().GetResult().ApiToken;

    // Mutation: the handler always calls CreateAppAsync -> 'agent create' is never seen and 'app create my-agent' is.
    [Fact(Timeout = 60_000)]
    public async Task Creates_the_agent_with_the_agent_noun_and_the_app_with_the_app_noun()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario(Fixture("agent-get.json")));
        var (_, handler, _, _) = HandlerHarness.BuildWithLogs(Declared(agent: true, app: true));

        await handler.ProvisionCatalystAsync(default);

        var agentCreate = Assert.Single(fake.Invocations, i => i is ["agent", "create", ..]);
        Assert.Equal(AgentName, agentCreate[2]);
        Assert.Contains("--ignore-if-exists", agentCreate);
        var appCreate = Assert.Single(fake.Invocations, i => i is ["app", "create", ..]);
        Assert.Equal(AppName, appCreate[2]);
        Assert.Equal("agent get", string.Join(' ', fake.Invocations.Single(i => i is ["agent", "get", ..]).Take(2)));
        Assert.Equal(AgentName, fake.Invocations.Single(i => i is ["agent", "get", ..])[2]);
        Assert.Equal(AppName, fake.Invocations.Single(i => i is ["app", "get", ..])[2]);
        Assert.DoesNotContain(fake.Invocations, i => i is ["appid", ..]);
    }

    // The agent's token exists ONLY at status.appIds[0].status.apiToken (the Agent status has no apiToken field).
    // Mutation: read only status.apiToken -> ResolveApiToken throws. The app and agent tokens differ, so a swap fails.
    [Fact(Timeout = 60_000)]
    public async Task Resolves_the_agent_token_from_the_embedded_appid_and_keeps_it_apart_from_the_apps()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario(Fixture("agent-get.json")));
        var (project, handler, _, _) = HandlerHarness.BuildWithLogs(Declared(agent: true, app: true));

        await handler.ProvisionCatalystAsync(default);

        Assert.DoesNotContain("\"apiToken\"", Fixture("agent-get.json").Split("\"appIds\"")[0], StringComparison.Ordinal);
        Assert.Equal(AgentToken, TokenOf(project, AgentName));
        Assert.Equal(AppToken, TokenOf(project, AppName));
    }

    // Mutation: drop the agent hint from ResolveApiToken, or swallow the missing token -> fails.
    [Fact(Timeout = 60_000)]
    public async Task An_agent_with_no_token_anywhere_fails_with_the_agent_api_hint_naming_it()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario(Fixture("agent-get-no-token.json")));
        var (_, handler, _, _) = HandlerHarness.BuildWithLogs(Declared());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ProvisionCatalystAsync(default));

        Assert.Contains(AgentName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Agent API may be unavailable", ex.Message, StringComparison.Ordinal);
    }

    // Mutation: swallow the agent create failure -> no exception. The create is asserted to have run (positive).
    [Fact(Timeout = 60_000)]
    public async Task Fails_clearly_when_the_deployment_has_no_agent_api()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario(Fixture("agent-get.json"),
            new ScenarioResponse { ArgsMatch = "agent create", ExitCode = 1, StdErr = "Error: 404 page not found" }));
        var (_, handler, states, _) = HandlerHarness.BuildWithLogs(Declared());

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => handler.ProvisionCatalystAsync(default));

        Assert.Contains(AgentName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("404", ex.Message, StringComparison.Ordinal);
        Assert.Contains(fake.Invocations, i => i is ["agent", "create", AgentName, ..]);
        Assert.DoesNotContain(fake.Invocations, i => i is ["agent", "get", ..]);
        await states.WaitFor(KnownResourceStates.FailedToStart, TimeSpan.FromSeconds(10));
    }

    // Mutation: leave the old "Ensuring applications" text -> fails.
    [Fact(Timeout = 60_000)]
    public async Task The_dashboard_state_for_this_step_is_identity_neutral()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Scenario(Fixture("agent-get.json")));
        var (_, handler, states, _) = HandlerHarness.BuildWithLogs(Declared());

        await handler.ProvisionCatalystAsync(default);
        await states.Drain();

        Assert.Contains("Ensuring identities", states.Snapshot());
        Assert.DoesNotContain("Ensuring applications", states.Snapshot());
    }
}
