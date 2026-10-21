using System.ComponentModel;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test that spawns builds a FakeDiagridFixture (or an EmptyPathFixture) first: a REAL diagrid may be
// installed, and a bare "diagrid" outside a fixture would reach it and make live calls against a real account.
[Collection(FakeDiagridCollection.Name)]
public class PreflightTests
{
    private static string VersionOutput(string version)
        // API server version comes FIRST and is far newer: a reader keyed on "contains version" would pick it up.
        => $"API server version: 1.181.0\nVersion: {version}\nGit commit: 3a524637\nGit version: 3a52463\nDate: 2026-10-20\n";

    private static ScenarioResponse Version(string version)
        => new() { ArgsMatch = "version", StdOut = VersionOutput(version) };

    private static ScenarioResponse Whoami(int exitCode = 0, string? stdErr = null)
        => new() { ArgsMatch = "whoami", ExitCode = exitCode, StdOut = exitCode == 0 ? "marc@example.com\n" : null, StdErr = stdErr };

    private static ScenarioResponse ProjectGetReady()
        => new() { ArgsMatch = "project get", StdOut = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "project-get-ready.json")) };

    private static ScenarioResponse ProjectGetPending()
        => new() { ArgsMatch = "project get", StdOut = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "project-get-pending.json")) };

    private static ScenarioResponse AnythingElseSucceeds() => new() { ExitCode = 0 };

    private static Scenario Healthy(string version = CatalystCli.MinimumVersion)
        => new() { Responses = [Version(version), Whoami(), AnythingElseSucceeds()] };

    // --- the version reader, on its own ---

    [Theory]
    [InlineData("1.85.0")]
    [InlineData("1.109.0")]
    [InlineData("v1.109.0")]
    [InlineData("1.109.0-rc.1")]
    [InlineData("2.0.0")]
    public void Versions_at_or_above_the_floor_pass(string version)
        // True, not merely "did not throw": an unreadable version also does not throw, but returns false.
        => Assert.True(CatalystCli.EnsureSupportedVersion(["version"], VersionOutput(version)));

    [Theory]
    [InlineData("1.84.9")]
    [InlineData("1.80.0")]
    [InlineData("0.99.0")]
    [InlineData("1.9.0")] // numeric, not lexical: 1.9 < 1.85
    public void Versions_below_the_floor_are_refused_naming_the_found_and_required_versions_and_the_capability(string version)
    {
        var ex = Assert.Throws<CatalystCliException>(() => CatalystCli.EnsureSupportedVersion(["version"], VersionOutput(version)));

        // The phrases, not bare substrings: swapping found and required would still contain both numbers.
        Assert.Contains($"is version {version},", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"needs {CatalystCli.MinimumVersion} or newer", ex.Message, StringComparison.Ordinal);
        Assert.Contains("--ignore-if-exists", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_floor_constant_changes_only_deliberately()
        => Assert.Equal("1.85.0", CatalystCli.MinimumVersion);

    [Theory]
    [InlineData("")]
    [InlineData("something went wrong\n")]
    [InlineData("Version: banana\n")]
    [InlineData("API server version: 1.181.0\n")] // only a differently-keyed line
    public void Output_without_a_readable_Version_line_reports_unreadable_instead_of_throwing(string stdOut)
        => Assert.False(CatalystCli.EnsureSupportedVersion(["version"], stdOut));

    [Fact(Timeout = 30_000)]
    public async Task An_unparseable_version_warns_and_proceeds()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ArgsMatch = "version", StdOut = "Version: banana\n" }, Whoami(), AnythingElseSucceeds()] });
        var logger = new CapturingLogger();

        await new CliCatalystProvisioner().Init(logger, default);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("no recognisable 'Version:' line", warning, StringComparison.Ordinal);
        // It went on to the next gate and past it.
        Assert.Contains(fake.Invocations, i => i is ["whoami", ..]);
        Assert.Contains(fake.Invocations, i => i is ["product", "use", "catalyst", ..]);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_version_command_that_fails_warns_and_proceeds()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ArgsMatch = "version", ExitCode = 2, StdErr = "boom\n" }, Whoami(), AnythingElseSucceeds()] });
        var logger = new CapturingLogger();

        await new CliCatalystProvisioner().Init(logger, default);

        Assert.Single(logger.Warnings);
        Assert.Contains(fake.Invocations, i => i is ["whoami", ..]);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_readable_supported_version_logs_no_warning()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Healthy());
        var logger = new CapturingLogger();

        await new CliCatalystProvisioner().Init(logger, default);

        Assert.Empty(logger.Warnings);
    }

    // The two real gates stay closed even though an unreadable version is open.
    [Fact(Timeout = 30_000)]
    public async Task An_unparseable_version_does_not_excuse_a_failed_login()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ArgsMatch = "version", StdOut = "Version: banana\n" }, Whoami(1, "not authenticated\n")] });

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => new CliCatalystProvisioner().Init(new CapturingLogger(), default));

        Assert.Contains("diagrid login", ex.Message, StringComparison.Ordinal);
    }

    // --- Init, against the fake ---

    [Fact(Timeout = 30_000)]
    public async Task Fails_with_an_actionable_message_when_not_logged_in()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [Version("1.85.0"), Whoami(1, "not authenticated\n"), AnythingElseSucceeds()] });

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => new CliCatalystProvisioner().Init(new CapturingLogger(), default));

        Assert.Contains("diagrid login", ex.Message, StringComparison.Ordinal);
        Assert.Contains("not authenticated", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, ex.ExitCode);
        // Nothing past the failed check may have run.
        Assert.DoesNotContain(fake.Invocations, i => i is ["product", ..]);
    }

    [Fact(Timeout = 30_000)]
    public async Task Fails_naming_the_minimum_version_when_the_cli_is_too_old()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Healthy("1.80.0"));

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => new CliCatalystProvisioner().Init(new CapturingLogger(), default));

        Assert.Contains("is version 1.80.0,", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"needs {CatalystCli.MinimumVersion} or newer", ex.Message, StringComparison.Ordinal);
        Assert.Contains("--ignore-if-exists", ex.Message, StringComparison.Ordinal);
        // A too-old CLI is refused before anything else is asked of it.
        Assert.Equal([["version", "--no-color"]], fake.Invocations);
    }

    [Fact(Timeout = 30_000)]
    public async Task Fails_with_an_actionable_message_when_the_cli_is_not_on_path()
    {
        using var empty = new EmptyPathFixture();

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => new CliCatalystProvisioner().Init(new CapturingLogger(), default));

        Assert.Contains("docs.diagrid.io", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PATH", ex.Message, StringComparison.Ordinal);
        Assert.IsType<Win32Exception>(ex.InnerException);
    }

    [Fact(Timeout = 30_000)]
    public async Task Checks_the_version_then_login_then_selects_the_product()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Healthy());

        await new CliCatalystProvisioner().Init(new CapturingLogger(), default);

        Assert.Equal(
            [["version", "--no-color"], ["whoami", "--no-color"], ["product", "use", "catalyst", "--no-color"]],
            fake.Invocations);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failing_product_use_does_not_fail_provisioning()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses = [Version("1.85.0"), Whoami(), new() { ArgsMatch = "product use", ExitCode = 1, StdErr = "boom\n" }],
        });

        await new CliCatalystProvisioner().Init(new CapturingLogger(), default);

        Assert.Contains(fake.Invocations, i => i is ["product", "use", "catalyst", ..]);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failing_project_use_does_not_fail_provisioning()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ArgsMatch = "project use", ExitCode = 1, StdErr = "boom\n" }] });

        await new CliCatalystProvisioner().UseProject("demo", default);

        Assert.Contains(fake.Invocations, i => i is ["project", "use", "demo", ..]);
    }

    // --- the handler: dashboard state and Ctrl-C ---

    [Fact(Timeout = 60_000)]
    public async Task Preflight_has_its_own_dashboard_state_before_any_provisioning()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [Version("1.85.0"), Whoami(), ProjectGetReady(), AnythingElseSucceeds()] });
        var (_, handler, states) = HandlerHarness.Build();

        await handler.ProvisionCatalyst(default);
        await states.Drain();

        var seen = states.Snapshot();
        var checking = seen.ToList().IndexOf(CatalystLifecycleHandler.CheckingCliState);
        var ensuring = seen.ToList().IndexOf("Ensuring project");
        Assert.True(checking >= 0, $"never saw '{CatalystLifecycleHandler.CheckingCliState}'; saw: {string.Join(", ", seen)}");
        Assert.True(checking < ensuring, $"preflight must come before provisioning; saw: {string.Join(", ", seen)}");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_preflight_failure_fails_the_resource_and_provisions_nothing()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [Version("1.85.0"), Whoami(1, "not authenticated\n"), AnythingElseSucceeds()] });
        var (_, handler, states) = HandlerHarness.Build();

        var ex = await Assert.ThrowsAsync<CatalystCliException>(() => handler.ProvisionCatalyst(default));
        await states.Drain();

        Assert.Contains("diagrid login", ex.Message, StringComparison.Ordinal);
        Assert.Contains(KnownResourceStates.FailedToStart, states.Snapshot());
        Assert.DoesNotContain(fake.Invocations, i => i is ["project", ..]);
    }

    // The warning has to reach the dashboard console: this is the test that fails if the handler hands Init a
    // different logger (the reason Init takes one at all).
    [Fact(Timeout = 60_000)]
    public async Task An_unparseable_version_warning_reaches_the_resource_console()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ArgsMatch = "version", StdOut = "Version: banana\n" }, Whoami(), ProjectGetReady(), AnythingElseSucceeds()] });
        var (_, handler, _, logs) = HandlerHarness.BuildWithLogs();

        await handler.ProvisionCatalyst(default);

        var lines = await logs.Collect();
        Assert.Contains(lines, l => l.Content.Contains("no recognisable 'Version:' line", StringComparison.Ordinal));
    }

    // Every site that catches in ProvisionCatalyst. Each case hangs the fake on that site's own command, waits until
    // that command has demonstrably started, then cancels. Ctrl-C is not a failure: no FailedToStart, and nothing in
    // the console (the generic catch also logs the error and its stack trace, which was the actual complaint).
    [Theory(Timeout = 120_000)]
    [InlineData("Init", "version")]
    [InlineData("CreateProject", "project create")]
    [InlineData("SelectProject", "project use")]
    [InlineData("LoadProjectDetails", "project get")]
    [InlineData("CreateApp", "app create")]
    [InlineData("CreatePubSub", "pubsub create")]
    [InlineData("CheckKvStore", "kv list")]
    [InlineData("CreateComponent", "component create")]
    [InlineData("CreateSubscription", "subscription create")]
    public async Task Cancelling_at_each_provisioning_step_is_quiet(string site, string hungCommand)
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses =
            [
                new() { ArgsMatch = hungCommand, SleepMilliseconds = 60_000 },
                .. HandlerHarness.PreflightPasses(),
                ProjectGetReady(),
                AnythingElseSucceeds(),
            ],
        });
        var (_, handler, states, logs) = HandlerHarness.BuildWithLogs(project =>
        {
            switch (site)
            {
                case "CreateApp":
                    project.Identities.Add(new ContainerResource("api"), new(IdentityKind.App, new()));
                    break;
                case "CreatePubSub":
                    project.PubSubs.Add("ps", new() { Project = "demo" });
                    break;
                case "CheckKvStore":
                    project.KvStores.Add("kv", new() { Project = "demo" });
                    break;
                case "CreateComponent":
                    project.Components.Add("c", new() { Name = "c", Type = "state.in-memory", Metadata = new Dictionary<string, object?> { ["k"] = "v" } });
                    break;
                case "CreateSubscription":
                    project.Subscriptions.Add("s", new() { Name = "s", Component = "ps", Topic = "t", Route = "/r" });
                    break;
            }
        });
        using var cts = new CancellationTokenSource();

        var provisioning = handler.ProvisionCatalyst(cts.Token);
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!fake.Invocations.Any(i => string.Join(' ', i).StartsWith(hungCommand, StringComparison.Ordinal)))
        {
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"'{hungCommand}' never ran; the {site} site was not reached");
            await Task.Delay(25);
        }

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provisioning);
        await states.Drain();
        Assert.DoesNotContain(KnownResourceStates.FailedToStart, states.Snapshot());
        var lines = await logs.Collect();
        Assert.DoesNotContain(lines, l => l.IsErrorMessage);
        Assert.DoesNotContain(lines, l => l.Content.Contains("OperationCanceledException", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get { lock (_warnings) return _warnings.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                lock (_warnings) _warnings.Add(formatter(state, exception));
            }
        }
    }

    /// <summary>
    /// Points PATH at an empty directory so no <c>diagrid</c> of any kind resolves, then restores it. Process-global,
    /// hence the FakeDiagrid collection. Nothing is spawned that could reach a real CLI: the lookup must fail.
    /// </summary>
    private sealed class EmptyPathFixture : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "empty-path-" + Guid.NewGuid().ToString("N"));
        private readonly string? _savedPath = Environment.GetEnvironmentVariable("PATH");

        public EmptyPathFixture()
        {
            Directory.CreateDirectory(_dir);
            Environment.SetEnvironmentVariable("PATH", _dir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", _savedPath);
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
