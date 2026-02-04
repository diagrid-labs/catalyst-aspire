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
        try
        {
            await Commands.CreateSubscription(descriptor.Name, new()
            {
                Project = projectName,
                Component = descriptor.Component,
                Topic = descriptor.Topic,
                Route = descriptor.Route,
                Scopes = descriptor.Scopes,
            }, cancellationToken);
        }
        catch
        {
            // todo: Would like a silent/idempotent create.
        }
    }

    public async Task CreateProject(string projectName, CancellationToken cancellationToken)
    {
        try
        {
            await Commands.CreateProject(projectName, cancellationToken: cancellationToken);
        }
        catch
        {
            // todo: Would like a silent/idempotent create.
        }
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

    public async Task CreateApp(string name, CancellationToken cancellationToken)
    {
        try
        {
            await Commands.CreateApp(name, cancellationToken: cancellationToken);
        }
        catch
        {
            // todo: Would like a silent/idempotent create.
        }
    }

    public async Task<AppDetails> GetAppDetails(string name, CancellationToken cancellationToken)
    {
        var appDetails = await Commands.GetAppDetails(name, cancellationToken);

        return new()
        {
            ApiToken = appDetails.Status.ApiToken,
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
