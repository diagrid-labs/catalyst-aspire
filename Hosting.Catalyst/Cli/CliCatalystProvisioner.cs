using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Options;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

public class CliCatalystProvisioner : CatalystProvisioner
{
    public async Task Init(ILogger logger, CancellationToken cancellationToken = default)
    {
        // Preflight, in this order: nothing below is worth running against a missing, too-old or signed-out CLI.
        await Commands.EnsureSupportedVersion(logger, cancellationToken);
        await Commands.EnsureLoggedIn(cancellationToken);

        // Best-effort courtesy: every command passes --project explicitly, so nothing depends on this succeeding.
        try
        {
            await Commands.UseCatalyst(cancellationToken);
        }
        catch (CatalystCliException)
        {
        }
    }

    public async Task CreateSubscription(string projectName, SubscriptionDescriptor descriptor, CancellationToken cancellationToken)
    {
        // subscription create has no --ignore-if-exists, so a failure here is a real failure: let it propagate.
        await Commands.CreateSubscription(descriptor.Name, new()
        {
            Project = projectName,
            Component = descriptor.Component,
            Topic = descriptor.Topic,
            Route = descriptor.Route,
            Scopes = descriptor.Scopes,
        }, cancellationToken);
    }

    public async Task CreateProject(string projectName, ProjectDescriptor descriptor, CancellationToken cancellationToken)
    {
        await Commands.CreateProject(projectName, ToCreateOptions(descriptor), cancellationToken);
    }

    internal static CreateProjectOptions ToCreateOptions(ProjectDescriptor descriptor) => new()
    {
        EnableManagedWorkflow = descriptor.EnableManagedWorkflow,
        Region = descriptor.Region,
        RegionGroup = descriptor.RegionGroup,
        DeployManagedKv = descriptor.DeployManagedKv,
        DeployManagedPubsub = descriptor.DeployManagedPubsub,
        GlobalIdMaxBodySize = descriptor.GlobalIdMaxBodySize,
        EnableWorkflowHistorySigning = descriptor.EnableWorkflowHistorySigning,
        DisableAppTunnels = descriptor.DisableAppTunnels,
    };

    public async Task UseProject(string projectName, CancellationToken cancellationToken)
    {
        // Best-effort courtesy: every command passes --project explicitly, so nothing depends on this succeeding.
        try
        {
            await Commands.UseProject(projectName, cancellationToken);
        }
        catch (CatalystCliException)
        {
        }
    }

    public async Task<ProjectDetails?> TryGetProjectDetails(string projectName, CancellationToken cancellationToken)
    {
        var cliDetails = await Commands.GetProjectDetails(projectName, cancellationToken);

        return cliDetails.ToProjectDetails();
    }

    private static readonly TimeSpan InitialPollDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxPollDelay = TimeSpan.FromSeconds(5);

    public async Task<ProjectDetails> WaitForProjectReady(
        string projectName,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        // --ignore-if-exists returns before --wait for an existing project, so a project still starting up on a
        // re-run reaches here without ever having been waited on.
        var stopwatch = Stopwatch.StartNew();
        var delay = InitialPollDelay;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var details = await TryGetProjectDetails(projectName, cancellationToken);

            if (details is not null)
            {
                return details;
            }

            var remaining = timeout - stopwatch.Elapsed;

            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException(
                    $"Catalyst project '{projectName}' did not report endpoints after {stopwatch.Elapsed.TotalSeconds:F0}s "
                    + "(it may still be starting; re-run once it is ready).");
            }

            await Task.Delay(delay < remaining ? delay : remaining, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(MaxPollDelay.Ticks, delay.Ticks * 2));
        }
    }

    public async Task CreateApp(string name, string projectName, CancellationToken cancellationToken)
    {
        // --ignore-if-exists (the CreateIdentityOptions default) makes this idempotent.
        await Commands.CreateIdentity(IdentityKind.App, name, new() { Project = projectName }, cancellationToken);
    }

    public async Task<AppDetails> GetAppDetails(string name, string projectName, CancellationToken cancellationToken)
    {
        var details = await Commands.GetIdentityDetails(IdentityKind.App, name, projectName, cancellationToken);

        return new()
        {
            ApiToken = details.ResolveApiToken(name, IdentityKind.App, $"project '{projectName}'"),
        };
    }

    public async Task UpsertComponent(ComponentDescriptor descriptor, string projectName, bool isManagedServiceComponent, CancellationToken cancellationToken)
    {
        var options = new CreateComponentOptions { Project = projectName };

        if (isManagedServiceComponent)
        {
            await Commands.UpdateComponent(descriptor, options, cancellationToken);
        }
        else
        {
            await Commands.CreateComponent(descriptor, options, cancellationToken);
        }
    }

    public async Task CreatePubSub(string name, PubSubDescriptor options, CancellationToken cancellationToken)
    {
        await Commands.CreatePubSub(name, new()
        {
            Project = options.Project,
            Scopes = options.Scopes,
        }, cancellationToken);

        // `pubsub create` also creates the fronting component, and is refused when a component of that name
        // already exists. --ignore-if-exists swallows that, so confirm the broker is really there.
        if (!await PubSubExists(name, options.Project, cancellationToken))
        {
            throw CollisionException("pubsub", name, options.Project);
        }
    }

    public async Task CreateKvStore(string name, KvStoreDescriptor options, CancellationToken cancellationToken)
    {
        await Commands.CreateKvStore(name, new()
        {
            Project = options.Project,
            Scopes = options.Scopes,
        }, cancellationToken);

        // `kv create` also creates the fronting component, and is refused when a component of that name
        // already exists. --ignore-if-exists swallows that, so confirm the store is really there.
        if (!await KvStoreExists(name, options.Project, cancellationToken))
        {
            throw CollisionException("kv", name, options.Project);
        }
    }

    public Task<bool> KvStoreExists(string name, string project, CancellationToken ct)
        => Commands.KvStoreExists(name, project, ct);

    public Task<bool> PubSubExists(string name, string project, CancellationToken ct)
        => Commands.PubSubExists(name, project, ct);

    private static CatalystCliException CollisionException(string noun, string name, string project)
    {
        var kind = noun == "kv" ? "KV store" : "pub/sub broker";
        return new CatalystCliException(
            [noun, "get", name, "--project", project],
            0,
            string.Empty,
            $"'diagrid {noun} create {name}' reported success but the {kind} '{name}' does not exist in project '{project}'. "
                + $"A component named '{name}' most likely already exists in that project, which blocks the {kind} "
                + $"(creating one also creates a component of the same name). Rename the {kind} in the AppHost, "
                + $"or remove the existing component '{name}' from the project, then run again.");
    }
}
