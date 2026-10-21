using System.Text.Json;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Options;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Hosting.Catalyst.Tests;

public class CommandArgumentTests
{
    [Fact]
    public void Create_app_uses_the_app_noun_and_current_flag_names()
        => Assert.Equal(
            ["app", "create", "test-api", "--project", "demo", "--endpoint", "http://localhost:5001",
             "--endpoint-token", "t", "--endpoint-protocol", "http", "--wait", "--ignore-if-exists"],
            Commands.BuildCreateIdentityArgs(IdentityKind.App, "test-api", new() {
                Project = "demo", Endpoint = "http://localhost:5001", EndpointToken = "t", EndpointProtocol = "http" }));

    [Fact]
    public void Create_agent_uses_the_agent_noun()
        => Assert.Equal(
            ["agent", "create", "my-agent", "--project", "demo", "--wait", "--ignore-if-exists"],
            Commands.BuildCreateIdentityArgs(IdentityKind.Agent, "my-agent", new() { Project = "demo" }));

    [Fact]
    public void Create_agent_omits_flags_that_only_app_create_accepts()
    {
        var args = Commands.BuildCreateIdentityArgs(IdentityKind.Agent, "my-agent", new()
        {
            Project = "demo",
            AppConfig = "cfg",
            MaxBodySize = "4Mi",
            EnableAppHealthCheck = true,
            AppHealthCheckPath = "/healthz",
            AppHealthProbeInterval = 5,
            AppHealthProbeTimeout = 5,
            AppHealthThreshold = 3,
            AppChannelTimeoutSeconds = 30,
            Labels = new Dictionary<string, string> { ["team"] = "orders" },
            Annotations = new Dictionary<string, string> { ["a"] = "b" },
            EnableWorkflowNotifications = ["FAILED"],
            EnableWorkflowArchive = ["COMPLETED"],
        });

        Assert.Equal(
            ["agent", "create", "my-agent", "--project", "demo", "--enable-workflow-archive", "COMPLETED",
             "--wait", "--ignore-if-exists"],
            args);
    }

    [Fact]
    public void Create_app_emits_every_optional_flag_with_the_current_names()
        => Assert.Equal(
            ["app", "create", "a", "--project", "demo", "--max-body-size", "4Mi", "--app-config", "cfg",
             "--enable-app-health-check", "--app-health-check-path", "/healthz",
             "--app-health-probe-interval", "5", "--app-health-probe-timeout", "6", "--app-health-threshold", "3",
             "--app-channel-timeout-seconds", "30",
             "--enable-workflow-archive", "COMPLETED", "--enable-workflow-archive", "FAILED",
             "--enable-workflow-notifications", "STALLED",
             "--label", "team=orders", "--annotation", "k=v", "--wait", "--ignore-if-exists"],
            Commands.BuildCreateIdentityArgs(IdentityKind.App, "a", new()
            {
                Project = "demo",
                MaxBodySize = "4Mi",
                AppConfig = "cfg",
                EnableAppHealthCheck = true,
                AppHealthCheckPath = "/healthz",
                AppHealthProbeInterval = 5,
                AppHealthProbeTimeout = 6,
                AppHealthThreshold = 3,
                AppChannelTimeoutSeconds = 30,
                EnableWorkflowArchive = ["COMPLETED", "FAILED"],
                EnableWorkflowNotifications = ["STALLED"],
                Labels = new Dictionary<string, string> { ["team"] = "orders" },
                Annotations = new Dictionary<string, string> { ["k"] = "v" },
            }));

    [Fact]
    public void Create_identity_can_opt_out_of_ignore_if_exists()
        => Assert.DoesNotContain("--ignore-if-exists",
            Commands.BuildCreateIdentityArgs(IdentityKind.App, "a", new() { Project = "demo", IgnoreIfExists = false }));

    [Fact]
    public void Get_identity_passes_project_explicitly()
        => Assert.Equal(["app", "get", "test-api", "--project", "demo", "--output", "json"],
            Commands.BuildGetIdentityArgs(IdentityKind.App, "test-api", "demo"));

    [Fact]
    public void Get_agent_uses_the_agent_noun()
        => Assert.Equal(["agent", "get", "a", "--project", "demo", "--output", "json"],
            Commands.BuildGetIdentityArgs(IdentityKind.Agent, "a", "demo"));

    [Fact]
    public void Create_project_is_idempotent()
        => Assert.Equal(
            ["project", "create", "demo", "--deploy-managed-pubsub=false", "--deploy-managed-kv=false",
             "--wait", "--ignore-if-exists"],
            Commands.BuildCreateProjectArgs("demo", new()));

    private static readonly ComponentDescriptor Component = new()
    {
        Name = "statestore",
        Type = "state.in-memory",
        Metadata = new Dictionary<string, object?> { ["k"] = "v" },
    };

    [Fact]
    public void Every_create_except_subscription_passes_ignore_if_exists_and_project()
    {
        string[][] creates =
        [
            Commands.BuildCreateProjectArgs("demo", new()),
            Commands.BuildCreateIdentityArgs(IdentityKind.App, "a", new() { Project = "demo" }),
            Commands.BuildCreateIdentityArgs(IdentityKind.Agent, "a", new() { Project = "demo" }),
            Commands.BuildCreateComponentArgs(Component, new() { Project = "demo" }),
            Commands.BuildCreatePubSubArgs("ps", new() { Project = "demo" }),
            Commands.BuildCreateKvStoreArgs("kv", new() { Project = "demo" }),
        ];

        Assert.All(creates, c => Assert.Contains("--ignore-if-exists", c));
        // project create names the project positionally; every other create targets one with --project.
        Assert.All(creates.Where(c => c[0] != "project"), c => Assert.Equal("demo", c[Array.IndexOf(c, "--project") + 1]));
    }

    [Fact]
    public void Subscription_create_has_no_ignore_if_exists_because_the_cli_has_no_such_flag()
    {
        var args = Commands.BuildCreateSubscriptionArgs("sub", new()
        {
            Project = "demo", Component = "ps", Topic = "t", Route = "/r",
        });

        Assert.DoesNotContain("--ignore-if-exists", args);
        Assert.Equal("demo", args[Array.IndexOf(args, "--project") + 1]);
    }

    [Fact]
    public void Existence_probes_are_get_calls_with_project()
    {
        Assert.Equal(["kv", "get", "statestore", "--project", "demo"], Commands.BuildGetKvStoreArgs("statestore", "demo"));
        Assert.Equal(["pubsub", "get", "pubsub", "--project", "demo"], Commands.BuildGetPubSubArgs("pubsub", "demo"));
    }

    [Fact]
    public void No_builder_includes_no_color_because_the_runner_appends_it()
        => Assert.DoesNotContain(AllBuilderOutputs(), b => b.Args.Contains("--no-color"));

    [Fact]
    public void Never_emits_the_hidden_appid_noun()
    {
        Assert.DoesNotContain("appid", AllBuilderOutputs().Select(b => b.Args[0]));
        Assert.DoesNotContain(AllBuilderOutputs(), b => b.Args.Contains("appid"));
    }

    // Every argument builder in Commands, by name. Identity builders appear once per kind.
    private static (string Name, string[] Args)[] AllBuilderOutputs() =>
    [
        (nameof(Commands.BuildUseCatalystArgs), Commands.BuildUseCatalystArgs()),
        (nameof(Commands.BuildVersionArgs), Commands.BuildVersionArgs()),
        (nameof(Commands.BuildWhoamiArgs), Commands.BuildWhoamiArgs()),
        (nameof(Commands.BuildUseProjectArgs), Commands.BuildUseProjectArgs("demo")),
        (nameof(Commands.BuildCreateProjectArgs), Commands.BuildCreateProjectArgs("demo", new())),
        (nameof(Commands.BuildGetProjectArgs), Commands.BuildGetProjectArgs("demo")),
        (nameof(Commands.BuildCreateIdentityArgs), Commands.BuildCreateIdentityArgs(IdentityKind.App, "a", new() { Project = "demo" })),
        (nameof(Commands.BuildCreateIdentityArgs), Commands.BuildCreateIdentityArgs(IdentityKind.Agent, "a", new() { Project = "demo" })),
        (nameof(Commands.BuildGetIdentityArgs), Commands.BuildGetIdentityArgs(IdentityKind.App, "a", "demo")),
        (nameof(Commands.BuildGetIdentityArgs), Commands.BuildGetIdentityArgs(IdentityKind.Agent, "a", "demo")),
        (nameof(Commands.BuildCreateComponentArgs), Commands.BuildCreateComponentArgs(Component, new() { Project = "demo" })),
        (nameof(Commands.BuildUpdateComponentArgs), Commands.BuildUpdateComponentArgs(Component, new() { Project = "demo" })),
        (nameof(Commands.BuildCreatePubSubArgs), Commands.BuildCreatePubSubArgs("ps", new() { Project = "demo" })),
        (nameof(Commands.BuildCreateKvStoreArgs), Commands.BuildCreateKvStoreArgs("kv", new() { Project = "demo" })),
        (nameof(Commands.BuildGetKvStoreArgs), Commands.BuildGetKvStoreArgs("kv", "demo")),
        (nameof(Commands.BuildGetPubSubArgs), Commands.BuildGetPubSubArgs("ps", "demo")),
        (nameof(Commands.BuildCreateSubscriptionArgs), Commands.BuildCreateSubscriptionArgs("sub", new() { Project = "demo", Component = "ps", Topic = "t", Route = "/r" })),
    ];

    [Fact]
    public void Every_argument_builder_in_Commands_is_listed_in_AllBuilderOutputs()
    {
        // Guards the guard. Keyed on names so a new builder cannot be waved through by bumping a count.
        // Any static method named Build*Args returning a string sequence counts, whatever its visibility.
        var reflected = typeof(Commands)
            .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(m => m.Name.StartsWith("Build") && m.Name.EndsWith("Args")
                        && typeof(IEnumerable<string>).IsAssignableFrom(m.ReturnType))
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal);

        var listed = AllBuilderOutputs().Select(b => b.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(reflected, listed);
    }

    private static CliResult Truncated(bool flag) => new("""{"status":{"apiToken":"abc""", "stderr", 0, flag);

    [Fact]
    public void ParseJson_reports_truncation_as_a_cli_exception_with_exit_code_zero()
    {
        var ex = Assert.Throws<CatalystCliException>(() => Commands.ParseJson(
            Truncated(true), ["app", "get", "a"], json => JsonSerializer.Deserialize<CliIdentityDetailsProbe>(json)));

        Assert.Equal(0, ex.ExitCode);
        Assert.Contains("cut off", ex.Message);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Fact]
    public void ParseJson_lets_the_original_json_exception_propagate_when_output_was_not_flagged_truncated()
        => Assert.Throws<JsonException>(() => Commands.ParseJson(
            Truncated(false), ["app", "get", "a"], json => JsonSerializer.Deserialize<CliIdentityDetailsProbe>(json)));

    private sealed record CliIdentityDetailsProbe(string? Status);
}
