using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using FakeDiagrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
[Collection(FakeDiagridCollection.Name)]
public class ReadinessTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    private static ScenarioResponse ProjectGetPending(int times = 0)
        => new() { ArgsMatch = "project get", Times = times, StdOut = Fixture("project-get-pending.json") };

    private static ScenarioResponse ProjectGetReady()
        => new() { ArgsMatch = "project get", StdOut = Fixture("project-get-ready.json") };

    private static int ProjectGets(FakeDiagridFixture fake)
        => fake.Invocations.Count(i => i is ["project", "get", ..]);

    [Fact(Timeout = 30_000)]
    public async Task Polls_until_the_project_reports_endpoints()
    {
        using var fake = new FakeDiagridFixture();
        // First project get is pending, every later one is ready.
        fake.SetScenario(new Scenario { Responses = [ProjectGetPending(times: 1), ProjectGetReady()] });

        var details = await new CliCatalystProvisioner()
            .WaitForProjectReady("demo", TimeSpan.FromSeconds(10), default);

        Assert.Equal(new Uri("https://http.r1.diagrid.io/v1/demo"), details.HttpEndpoint);
        Assert.Equal(new Uri("https://grpc.r1.diagrid.io:7443/demo"), details.GrpcEndpoint);
        Assert.Equal(2, ProjectGets(fake));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_project_that_is_already_ready_is_read_once_without_polling()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [ProjectGetReady()] });

        await new CliCatalystProvisioner().WaitForProjectReady("demo", TimeSpan.FromSeconds(10), default);

        Assert.Equal(1, ProjectGets(fake));
    }

    [Fact(Timeout = 30_000)]
    public async Task TryGetProjectDetails_returns_null_for_a_pending_project_instead_of_throwing()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [ProjectGetPending()] });

        Assert.Null(await new CliCatalystProvisioner().TryGetProjectDetails("demo", default));
    }

    [Fact(Timeout = 30_000)]
    public async Task Timing_out_names_the_project_and_the_time_waited()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [ProjectGetPending()] });

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => new CliCatalystProvisioner().WaitForProjectReady("demo", TimeSpan.FromSeconds(2), default));

        Assert.Contains("'demo'", ex.Message, StringComparison.Ordinal);
        Assert.Matches(@"after \d+s", ex.Message);
        Assert.True(ProjectGets(fake) >= 2, "it should have polled more than once before giving up");
    }

    // Review Focus #4: Ctrl-C mid-provision.
    [Fact(Timeout = 30_000)]
    public async Task Cancellation_during_the_wait_stops_promptly()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [ProjectGetPending()] });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CliCatalystProvisioner().WaitForProjectReady("demo", TimeSpan.FromMinutes(5), cts.Token));

        // The 5 minute timeout is the mutation this catches: a loop that ignored the token would run to it.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
    }

    // The handler path: LoadProjectDetailsAsync is what production runs, so this is the test that fails if it
    // stops calling WaitForProjectReady (a bare TryGetProjectDetails would hand it null, a bare parse would throw).
    [Fact(Timeout = 60_000)]
    public async Task Provisioning_waits_for_a_starting_project_then_publishes_its_endpoints()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses = [ProjectGetPending(times: 2), ProjectGetReady(), new() { ExitCode = 0 }],
        });
        var (project, handler, states) = Build();

        await handler.ProvisionCatalyst(default);

        Assert.Equal("https://http.r1.diagrid.io/v1/demo", await project.HttpEndpoint.Task);
        Assert.Equal("https://grpc.r1.diagrid.io:7443/demo", await project.GrpcEndpoint.Task);
        Assert.Equal(3, ProjectGets(fake));
        Assert.Contains(CatalystLifecycleHandler.WaitingForProjectState, states.Snapshot());
    }

    [Fact(Timeout = 60_000)]
    public async Task Cancelling_provisioning_while_waiting_stops_promptly_and_never_reports_the_project_ready()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [ProjectGetPending(), new() { ExitCode = 0 }] });
        var (project, handler, states) = Build();
        using var cts = new CancellationTokenSource();

        var provisioning = handler.ProvisionCatalyst(cts.Token);
        // Cancel only once the handler is demonstrably inside the wait, however slow the machine is.
        await states.WaitFor(CatalystLifecycleHandler.WaitingForProjectState, TimeSpan.FromSeconds(20));
        var stopwatch = Stopwatch.StartNew();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provisioning);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
        var seen = states.Snapshot();
        Assert.DoesNotContain(KnownResourceStates.Running, seen);
        Assert.DoesNotContain(KnownResourceStates.Active, seen);
        Assert.DoesNotContain(KnownResourceStates.Finished, seen);
        Assert.False(project.HttpEndpoint.Task.IsCompleted, "endpoints must not be published for a project that never became ready");
        Assert.NotEqual(KnownResourceStates.Active, seen[^1]);
    }

    private static (CatalystProject Project, CatalystLifecycleHandler Handler, StateRecorder States) Build()
    {
        var project = new CatalystProject { ProjectName = "demo" };
        var loggerService = new ResourceLoggerService();
        var notifications = new ResourceNotificationService(
            NullLogger<ResourceNotificationService>.Instance,
            new HostLifetimeStub(),
            new EmptyServiceProvider(),
            loggerService);
        var handler = new CatalystLifecycleHandler(project, loggerService, notifications, new CliCatalystProvisioner());

        return (project, handler, new StateRecorder(notifications));
    }

    /// <summary>Records every state text published for the resource, via the real notification surface.</summary>
    private sealed class StateRecorder
    {
        private readonly List<string> _states = [];
        private readonly CancellationTokenSource _stop = new();

        public StateRecorder(ResourceNotificationService notifications)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var e in notifications.WatchAsync(_stop.Token))
                    {
                        if (e.Snapshot.State?.Text is { } text)
                        {
                            lock (_states) _states.Add(text);
                        }
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_states) return _states.ToList();
        }

        public async Task WaitFor(string state, TimeSpan timeout)
        {
            var deadline = Stopwatch.StartNew();
            while (!Snapshot().Contains(state))
            {
                if (deadline.Elapsed > timeout) throw new TimeoutException($"never saw state '{state}'; saw: {string.Join(", ", Snapshot())}");
                await Task.Delay(25);
            }
        }
    }

    private sealed class HostLifetimeStub : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
