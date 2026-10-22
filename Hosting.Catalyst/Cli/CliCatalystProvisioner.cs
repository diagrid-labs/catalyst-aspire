using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Options;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using Microsoft.Extensions.Logging;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

public class CliCatalystProvisioner : ICatalystProvisioner
{
    public async Task InitializeAsync(ILogger logger, CancellationToken cancellationToken = default)
    {
        // Preflight, in this order: nothing below is worth running against a missing, too-old or signed-out CLI.
        await Commands.EnsureSupportedVersionAsync(logger, cancellationToken);
        await Commands.EnsureLoggedInAsync(cancellationToken);

        // Best-effort courtesy: every command passes --project explicitly, so nothing depends on this succeeding.
        try
        {
            await Commands.UseCatalystAsync(cancellationToken);
        }
        catch (CatalystCliException)
        {
        }
    }

    public async Task UpsertSubscriptionAsync(string projectName, SubscriptionDescriptor descriptor, CancellationToken cancellationToken)
    {
        var options = new CreateSubscriptionOptions
        {
            Project = projectName,
            Component = descriptor.Component,
            Topic = descriptor.Topic,
            Route = descriptor.Route,
            Scopes = descriptor.Scopes,
            Bulk = descriptor.Bulk,
            BulkMaxMessagesCount = descriptor.BulkMaxMessagesCount,
            BulkMaxAwaitDuration = descriptor.BulkMaxAwaitDuration,
        };

        // subscription create has no --ignore-if-exists, so probe. The probe reads only the exit code, and the CLI
        // exits 1 for every error: a failed probe falls through to create, which fails loudly if the cause was real.
        if (await Commands.SubscriptionExistsAsync(descriptor.Name, projectName, cancellationToken))
        {
            await Commands.UpdateSubscriptionAsync(descriptor.Name, options, cancellationToken);
        }
        else
        {
            await Commands.CreateSubscriptionAsync(descriptor.Name, options, cancellationToken);
        }
    }

    public async Task CreateProjectAsync(string projectName, ProjectDescriptor descriptor, CancellationToken cancellationToken)
    {
        await Commands.CreateProjectAsync(projectName, ToCreateOptions(descriptor), cancellationToken);
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

    public async Task UseProjectAsync(string projectName, CancellationToken cancellationToken)
    {
        // Best-effort courtesy: every command passes --project explicitly, so nothing depends on this succeeding.
        try
        {
            await Commands.UseProjectAsync(projectName, cancellationToken);
        }
        catch (CatalystCliException)
        {
        }
    }

    public async Task<ProjectDetails?> TryGetProjectDetailsAsync(string projectName, CancellationToken cancellationToken)
    {
        var cliDetails = await Commands.GetProjectDetailsAsync(projectName, cancellationToken);

        return cliDetails.ToProjectDetails();
    }

    private static readonly TimeSpan InitialPollDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxPollDelay = TimeSpan.FromSeconds(5);

    public async Task<ProjectDetails> WaitForProjectReadyAsync(
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

            var details = await TryGetProjectDetailsAsync(projectName, cancellationToken);

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

    public async Task CreateAppAsync(string name, string projectName, CancellationToken cancellationToken)
    {
        // --ignore-if-exists (the CreateIdentityOptions default) makes this idempotent.
        await Commands.CreateIdentityAsync(IdentityKind.App, name, new() { Project = projectName }, cancellationToken);
    }

    public async Task<AppDetails> GetAppDetailsAsync(string name, string projectName, CancellationToken cancellationToken)
    {
        var details = await Commands.GetIdentityDetailsAsync(IdentityKind.App, name, projectName, cancellationToken);

        return new()
        {
            ApiToken = details.ResolveApiToken(name, IdentityKind.App, $"project '{projectName}'"),
        };
    }

    public async Task CreateAgentAsync(string name, string projectName, CancellationToken cancellationToken)
    {
        await Commands.CreateIdentityAsync(IdentityKind.Agent, name, new() { Project = projectName }, cancellationToken);
    }

    public async Task<AppDetails> GetAgentDetailsAsync(string name, string projectName, CancellationToken cancellationToken)
    {
        var details = await Commands.GetIdentityDetailsAsync(IdentityKind.Agent, name, projectName, cancellationToken);

        return new()
        {
            ApiToken = details.ResolveApiToken(name, IdentityKind.Agent, $"project '{projectName}'"),
        };
    }

    public async Task UpsertComponentAsync(ComponentDescriptor descriptor, string projectName, bool isManagedServiceComponent, CancellationToken cancellationToken)
    {
        var options = new CreateComponentOptions { Project = projectName };

        if (isManagedServiceComponent)
        {
            await Commands.UpdateComponentAsync(descriptor, options, cancellationToken);
        }
        else
        {
            await Commands.CreateComponentAsync(descriptor, options, cancellationToken);
        }
    }

    public async Task CreatePubSubAsync(string name, PubSubDescriptor options, CancellationToken cancellationToken)
    {
        await Commands.CreatePubSubAsync(name, new()
        {
            Project = options.Project,
            Scopes = options.Scopes,
        }, cancellationToken);

        // `pubsub create` also creates the fronting component, and is refused when a component of that name
        // already exists. --ignore-if-exists swallows that, so confirm the broker is really there.
        if (!await PubSubExistsAsync(name, options.Project, cancellationToken))
        {
            throw CollisionException("pubsub", name, options.Project);
        }
    }

    public async Task CreateKvStoreAsync(string name, KvStoreDescriptor options, CancellationToken cancellationToken)
    {
        await Commands.CreateKvStoreAsync(name, new()
        {
            Project = options.Project,
            Scopes = options.Scopes,
        }, cancellationToken);

        // `kv create` also creates the fronting component, and is refused when a component of that name
        // already exists. --ignore-if-exists swallows that, so confirm the store is really there.
        if (!await KvStoreExistsAsync(name, options.Project, cancellationToken))
        {
            throw CollisionException("kv", name, options.Project);
        }
    }

    public Task<bool> KvStoreExistsAsync(string name, string projectName, CancellationToken cancellationToken)
        => Commands.KvStoreExistsAsync(name, projectName, cancellationToken);

    public Task<bool> PubSubExistsAsync(string name, string projectName, CancellationToken cancellationToken)
        => Commands.PubSubExistsAsync(name, projectName, cancellationToken);

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
