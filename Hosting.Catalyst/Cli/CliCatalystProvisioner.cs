using System;
using System.Threading;
using System.Threading.Tasks;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

public class CliCatalystProvisioner : CatalystProvisioner
{
    public async Task Init(CancellationToken cancellationToken = default)
    {
        // todo: Need to not have to do this.
        await Commands.UseCatalyst(cancellationToken);
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
        await Commands.CreateProject(projectName, new()
        {
            EnableManagedWorkflow = descriptor.EnableManagedWorkflow,
            DeployManagedKv = false,
            DeployManagedPubsub = false,
        }, cancellationToken);
    }

    public async Task UseProject(string projectName, CancellationToken cancellationToken)
    {
        await Commands.UseProject(projectName, cancellationToken);
    }

    public async Task<ProjectDetails> GetProjectDetails(string projectName, CancellationToken cancellationToken)
    {
        var cliDetails = await Commands.GetProjectDetails(projectName, cancellationToken);

        return new()
        {
            HttpEndpoint = cliDetails.Status.Endpoints.Http.Uri,
            GrpcEndpoint = cliDetails.Status.Endpoints.Grpc.Uri,
        };
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

    public async Task CreateComponent(ComponentDescriptor descriptor, string projectName, CancellationToken cancellationToken)
    {
        await Commands.CreateComponent(descriptor, new()
        {
            Project = projectName,
        }, cancellationToken);
    }

    public async Task CreatePubSub(string name, PubSubDescriptor options, CancellationToken cancellationToken)
    {
        await Commands.CreatePubSub(name, new()
        {
            Project = options.Project,
            Scopes = options.Scopes,
        }, cancellationToken);
    }

    public async Task CreateKvStore(string name, KvStoreDescriptor options, CancellationToken cancellationToken)
    {
        await Commands.CreateKvStore(name, new()
        {
            Project = options.Project,
            Scopes = options.Scopes,
        }, cancellationToken);
    }

    public async Task<bool> CheckKvStoreExists(string name, string projectName, CancellationToken cancellationToken)
    {
        return await Commands.CheckKvStoreExists(name, projectName, cancellationToken);
    }
}
