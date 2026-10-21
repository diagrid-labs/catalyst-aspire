using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hosting.Catalyst.IntegrationTests;

/// <summary>Builds the real lifecycle handler over the real CLI provisioner, with a recorder on its dashboard states.</summary>
internal static class HandlerHarness
{
    /// <summary>Scenario responses that let the CLI preflight (version, then whoami) pass, for tests of later steps.</summary>
    internal static FakeDiagrid.ScenarioResponse[] PreflightPasses() =>
    [
        new() { ArgsMatch = "version", StdOut = "Version: 1.85.0\n" },
        new() { ArgsMatch = "whoami", StdOut = "marc@example.com\n" },
    ];

    internal static (CatalystProject Project, CatalystLifecycleHandler Handler, StateRecorder States) Build()
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
    internal sealed class StateRecorder
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

        /// <summary>Waits until no new state has arrived for a quiet period (bounded), so absence checks are not vacuous.</summary>
        public async Task Drain()
        {
            var deadline = Stopwatch.StartNew();
            var count = Snapshot().Count;
            var quiet = Stopwatch.StartNew();
            while (quiet.Elapsed < TimeSpan.FromMilliseconds(500) && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(25);
                var now = Snapshot().Count;
                if (now != count) { count = now; quiet.Restart(); }
            }
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
