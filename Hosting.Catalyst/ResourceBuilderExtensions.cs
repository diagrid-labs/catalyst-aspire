using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Hosting.Catalyst.ComponentSpec;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.Aspire.Hosting.Catalyst;

public static class ResourceBuilderExtensions
{
    public const string SampleKvName = "statestore";
    public const string SamplePubsubName = "pubsub";
    
    /// <summary>
    ///     Associates an Aspire orchestration to a Catalyst project.
    /// </summary>
    /// <param name="applicationBuilder"></param>
    /// <param name="customProjectName"></param>
    public static IResourceBuilder<CatalystProject> AddCatalystProject(this IDistributedApplicationBuilder applicationBuilder, string? customProjectName = null, ProjectDescriptor? descriptor = null)
    {
        applicationBuilder.Services.AddSingleton<CatalystProvisioner, CliCatalystProvisioner>();

        var projectName = ResolveProjectName(customProjectName, Assembly.GetEntryAssembly()?.EntryPoint?.DeclaringType?.Namespace);

        // todo: Custom icon, support pending https://github.com/dotnet/aspire/issues/8684
        var catalystProject = applicationBuilder.AddResource(new CatalystProject
        {
            ProjectName = projectName,
            ProjectDescriptor = descriptor ?? new(),
        });

        catalystProject.WithAnnotation(new ResourceUrlAnnotation
        {
            Url = DashboardUrl(Environment.GetEnvironmentVariable(DiagridUrlVariable)),
            DisplayText = "Catalyst Dashboard",
        });

        catalystProject.OnInitializeResource(async (resource, initializeResourceEvent, cancellationToken) =>
        {
            var eventing = initializeResourceEvent.Eventing;
            var services = initializeResourceEvent.Services;
            
            // note: This is necessary so that anything that has an outstanding .WaitFor will be notified.
            await eventing.PublishAsync(new BeforeResourceStartedEvent(resource, services), cancellationToken);

            await new CatalystLifecycleHandler(
                resource,
                initializeResourceEvent.Services.GetRequiredService<ResourceLoggerService>(),
                initializeResourceEvent.Services.GetRequiredService<ResourceNotificationService>(),
                initializeResourceEvent.Services.GetRequiredService<CatalystProvisioner>()
            )
                .ProvisionCatalyst(cancellationToken);
        });

        return catalystProject;
    }

    private const string DiagridUrlVariable = "DIAGRID_URL";
    private const string DefaultApiUrl = "https://api.r1.diagrid.io/";

    /// <summary>
    ///     Uses the given name, else the entry point's namespace, and throws rather than inventing a name.
    /// </summary>
    internal static string ResolveProjectName(string? customProjectName, string? entryPointNamespace)
    {
        if (!string.IsNullOrWhiteSpace(customProjectName))
            return customProjectName;

        if (!string.IsNullOrWhiteSpace(entryPointNamespace))
            return entryPointNamespace.Replace(".", "-").ToLowerInvariant();

        throw new ArgumentException(
            "The Catalyst project name could not be inferred from the entry assembly. Pass it explicitly to AddCatalystProject.",
            "customProjectName");
    }

    /// <summary>
    ///     Mirrors the Diagrid CLI's <c>ProductConsoleURL</c> for the <c>catalyst</c> product, driven by <c>DIAGRID_URL</c>
    ///     (default <c>https://api.r1.diagrid.io/</c>).
    /// </summary>
    internal static string DashboardUrl(string? apiUrl)
    {
        var url = string.IsNullOrWhiteSpace(apiUrl) ? DefaultApiUrl : apiUrl;
        var host = url.Contains("staging.diagrid.dev", StringComparison.Ordinal) ? "catalyst.staging.diagrid.dev"
            : url.Contains(".dev.diagrid.io", StringComparison.Ordinal) ? "catalyst.dev.diagrid.io"
            : url.Contains(".stg.diagrid.io", StringComparison.Ordinal) ? "catalyst.stg.diagrid.io"
            : url.Contains(".local.diagrid.io", StringComparison.Ordinal) || url.Contains(".7f000001.nip.io", StringComparison.Ordinal) ? "catalyst.local.diagrid.io"
            : "catalyst.diagrid.io";

        return $"https://{host}/admin/organization";
    }

    /// <summary>
    ///     Configures a resource to use Catalyst. Equivalent to <see cref="WithCatalystApp{ResourceType}"/>.
    /// </summary>
    /// <param name="resourceBuilder"></param>
    /// <param name="catalystProjectBuilder"></param>
    public static IResourceBuilder<ResourceType> WithCatalyst<ResourceType>(this IResourceBuilder<ResourceType> resourceBuilder, IResourceBuilder<CatalystProject>? catalystProjectBuilder = null)
    where ResourceType : Resource, IResourceWithEnvironment, IResourceWithWaitSupport
        => resourceBuilder.WithCatalystApp(catalystProjectBuilder);

    /// <summary>
    ///     Configures a resource to run as a Catalyst app. Resources without an HTTP endpoint,
    ///     such as a workflow-only worker, are supported.
    /// </summary>
    /// <remarks>
    ///     Call this after the resource's HTTP endpoint is declared (for example <c>WithHttpEndpoint</c>). The endpoint is
    ///     read when this method runs, so an endpoint added afterwards is not seen and the proxy is started without
    ///     <c>--app-port</c>.
    /// </remarks>
    /// <param name="resourceBuilder"></param>
    /// <param name="catalystProjectBuilder"></param>
    /// <exception cref="ArgumentException">The Aspire resource name is not a valid Catalyst name.</exception>
    public static IResourceBuilder<ResourceType> WithCatalystApp<ResourceType>(this IResourceBuilder<ResourceType> resourceBuilder, IResourceBuilder<CatalystProject>? catalystProjectBuilder = null)
    where ResourceType : Resource, IResourceWithEnvironment, IResourceWithWaitSupport
        => resourceBuilder.WithCatalystIdentity(IdentityKind.App, nameof(WithCatalystApp), catalystProjectBuilder);

    /// <summary>
    ///     Configures a resource to run as a Catalyst agent (<c>diagrid agent create</c>) rather than an app. The proxy
    ///     wiring is the same as for an app. Creating an agent provisions the agent registry itself, so the project does
    ///     not need to have been created with a managed KV store.
    /// </summary>
    /// <remarks>
    ///     Call this after the resource's HTTP endpoint is declared (for example <c>WithHttpEndpoint</c>). Agent support
    ///     depends on the Catalyst deployment: when it has no agent API, provisioning fails naming the resource.
    /// </remarks>
    /// <param name="resourceBuilder"></param>
    /// <param name="catalystProjectBuilder"></param>
    /// <exception cref="ArgumentException">The Aspire resource name is not a valid Catalyst name.</exception>
    public static IResourceBuilder<ResourceType> WithCatalystAgent<ResourceType>(this IResourceBuilder<ResourceType> resourceBuilder, IResourceBuilder<CatalystProject>? catalystProjectBuilder = null)
    where ResourceType : Resource, IResourceWithEnvironment, IResourceWithWaitSupport
        => resourceBuilder.WithCatalystIdentity(IdentityKind.Agent, nameof(WithCatalystAgent), catalystProjectBuilder);

    private static IResourceBuilder<ResourceType> WithCatalystIdentity<ResourceType>(
        this IResourceBuilder<ResourceType> resourceBuilder,
        IdentityKind kind,
        string publicMethodName,
        IResourceBuilder<CatalystProject>? catalystProjectBuilder)
    where ResourceType : Resource, IResourceWithEnvironment, IResourceWithWaitSupport
    {
        CatalystNames.EnsureValidResourceName(resourceBuilder.Resource.Name);

        var applicationBuilder = resourceBuilder.ApplicationBuilder;
        var catalystProjects = applicationBuilder.EnsureCatalystResources();

        if (catalystProjects.Count > 1 && catalystProjectBuilder is null)
            throw new($"Your Aspire orchestration has multiple Catalyst projects configured. Please specify which one to use when calling {publicMethodName}.");

        catalystProjectBuilder ??= applicationBuilder.CreateResourceBuilder(catalystProjects.Single());

        resourceBuilder.WaitForCompletion(catalystProjectBuilder);

        EndpointReference? httpEndpoint = resourceBuilder.Resource is IResourceWithEndpoints resourceWithEndpoints
            ? resourceWithEndpoints.GetEndpoints().FirstOrDefault((endpoint) => endpoint.IsHttp)
            : null;

        var proxyName = $"{resourceBuilder.Resource.Name}-catalyst-proxy";
        IResourceBuilder<ExecutableResource> catalystProxy;

        try
        {
            catalystProxy = applicationBuilder.AddExecutable(
                proxyName,
                "diagrid",
                applicationBuilder.AppHostDirectory,
                BuildDevRunArgs(catalystProjectBuilder.Resource.ProjectName, resourceBuilder.Resource.Name, httpEndpoint)
            );
        }
        catch (ArgumentException ex)
        {
            // Aspire limits resource names to 64 characters, and the proxy name adds a suffix to the resource's own.
            // Name the resource the developer typed rather than the proxy they never see.
            throw new ArgumentException(
                $"The Aspire resource '{resourceBuilder.Resource.Name}' is too long to use with Catalyst: Catalyst runs it behind a proxy "
                + $"resource named '{proxyName}', which Aspire rejects ({ex.Message}). Shorten the resource name.",
                nameof(resourceBuilder),
                ex);
        }

        catalystProxy.WaitForCompletion(catalystProjectBuilder);

        resourceBuilder
            .WaitFor(catalystProxy)
            .WithChildRelationship(catalystProxy);

        catalystProjectBuilder.Resource.Identities[resourceBuilder.Resource] = new(kind, new());

        resourceBuilder.WithEnvironment(async (context) =>
        {
            var applicationModel = (DistributedApplicationModel) context.ExecutionContext.ServiceProvider
                .GetRequiredService(typeof(DistributedApplicationModel));

            var catalystProject = applicationModel.Resources.FirstOrDefault((resource) => resource is CatalystProject) as CatalystProject
                ?? throw new("This project is missing a Catalyst project resource.");

            var appDetails = await catalystProject.Identities[resourceBuilder.Resource].Details.Task;

            context.EnvironmentVariables["DAPR_GRPC_ENDPOINT"] = await catalystProject.GrpcEndpoint.Task;
            context.EnvironmentVariables["DAPR_HTTP_ENDPOINT"] = await catalystProject.HttpEndpoint.Task;
            context.EnvironmentVariables["DAPR_API_TOKEN"] = appDetails.ApiToken;
        });

        return resourceBuilder;
    }

    /// <summary>
    ///     Builds the <c>diagrid dev run</c> arguments. <c>--app-port</c> is omitted when the resource has no
    ///     HTTP endpoint, as for a workflow-only worker.
    /// </summary>
    internal static object[] BuildDevRunArgs(string projectName, string id, EndpointReference? httpEndpoint)
    {
        var args = new List<object>
        {
            "dev", "run",
            "--approve",
            "--project", projectName,
            "--id", id,
        };

        if (httpEndpoint is not null)
        {
            args.Add("--app-port");
            args.Add(httpEndpoint.Property(EndpointProperty.Port));
        }

        args.Add("--skip-managed-kv");
        args.Add("--skip-managed-pubsub");

        return args.ToArray();
    }

    public static IResourceBuilder<ResourceType> WithSubscription<ResourceType>(
        this IResourceBuilder<ResourceType> resourceBuilder,
        string name,
        string component,
        string topic,
        string route
    ) where ResourceType : Resource, IResourceWithEnvironment, IResourceWithWaitSupport
        => resourceBuilder.WithSubscription(name, component, topic, route, bulk: false, bulkMaxMessagesCount: null, bulkMaxAwaitDuration: null);

    /// <summary>
    ///     Adds a subscription that delivers messages in bulk.
    /// </summary>
    /// <param name="resourceBuilder"></param>
    /// <param name="name">The Catalyst subscription name; must be unique in the project.</param>
    /// <param name="component">The pub/sub component.</param>
    /// <param name="topic"></param>
    /// <param name="route"></param>
    /// <param name="bulk">Whether to enable bulk subscription.</param>
    /// <param name="bulkMaxMessagesCount">The most messages in one bulk delivery, or null for the Catalyst default.</param>
    /// <param name="bulkMaxAwaitDuration">The longest to wait for a bulk delivery to fill, or null for the Catalyst default.</param>
    /// <typeparam name="ResourceType"></typeparam>
    public static IResourceBuilder<ResourceType> WithSubscription<ResourceType>(
        this IResourceBuilder<ResourceType> resourceBuilder,
        string name,
        string component,
        string topic,
        string route,
        bool bulk,
        int? bulkMaxMessagesCount,
        TimeSpan? bulkMaxAwaitDuration
    ) where ResourceType : Resource, IResourceWithEnvironment, IResourceWithWaitSupport
    {
        var applicationBuilder = resourceBuilder.ApplicationBuilder;
        var catalystProject = applicationBuilder
            .EnsureCatalystResources()
            .FirstOrDefault((catalystProject) => catalystProject.Identities.Any((app) => app.Key == resourceBuilder.Resource));

        if (catalystProject is null)
            throw new("This resource is not associated with a Catalyst project.");
        
        var appName = resourceBuilder.Resource.Name;

        // Same app, component and topic is a duplicate whatever it is named: two subscriptions would deliver the topic twice.
        var duplicate = catalystProject.Subscriptions.Values.FirstOrDefault(existing =>
            existing.Scopes.Contains(appName) && existing.Component == component && existing.Topic == topic);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"The app '{appName}' already subscribes to topic '{topic}' on component '{component}' (subscription '{duplicate.Name}'). "
                + "Each app, component and topic combination can have only one subscription.",
                nameof(topic));
        }

        if (catalystProject.Subscriptions.TryGetValue(name, out var clash))
        {
            throw new ArgumentException(
                $"The subscription name '{name}' is already used for topic '{clash.Topic}' on component '{clash.Component}'; "
                + $"it cannot also be used for app '{appName}', component '{component}', topic '{topic}'. Give one of them a different name.",
                nameof(name));
        }

        catalystProject.Subscriptions.Add(name, new()
        {
            Name = name,
            Component = component,
            Topic = topic,
            Route = route,
            Scopes =
            [
                appName,
            ],
            Bulk = bulk,
            BulkMaxMessagesCount = bulkMaxMessagesCount,
            BulkMaxAwaitDuration = bulkMaxAwaitDuration,
        });

        return resourceBuilder;
    }
    
    /// <summary>
    ///     Adds a subscription to the sample pubsub component and service.
    /// </summary>
    /// <param name="resourceBuilder"></param>
    /// <param name="topic"></param>
    /// <param name="path"></param>
    /// <typeparam name="ResourceType"></typeparam>
    /// <returns></returns>
    public static IResourceBuilder<ResourceType> WithSubscription<ResourceType>(this IResourceBuilder<ResourceType> resourceBuilder, string topic, string path)
    where ResourceType : Resource, IResourceWithEnvironment, IResourceWithWaitSupport
    {
        // The topic is part of the name: two topics on one app must not collide on the project's subscription names.
        var name = CatalystNames.SubscriptionName(resourceBuilder.Resource.Name, SamplePubsubName, topic);

        return resourceBuilder.WithSubscription(name, SamplePubsubName, topic, path);
    }
    
    /// <summary>
    ///     Adds a weakly-typed component to the Catalyst project.
    /// </summary>
    /// <param name="catalystProject"></param>
    /// <param name="name"></param>
    /// <param name="type"></param>
    /// <param name="metadata"></param>
    /// <param name="scopes"></param>
    public static IResourceBuilder<CatalystProject> WithComponent(
        this IResourceBuilder<CatalystProject> catalystProject,
        string name,
        string type,
        IDictionary<string, object?> metadata,
        IList<string> scopes
    )
    {
        catalystProject.Resource.Components.Add(name, new()
        {
            Name = name,
            Type = type,
            Scopes = scopes,
            Metadata = metadata,
        });

        return catalystProject;
    }

    /// <summary>
    ///     Adds a strongly-typed component to the Catalyst project.
    /// </summary>
    /// <param name="catalystProject"></param>
    /// <param name="name"></param>
    /// <param name="component"></param>
    /// <typeparam name="MetadataType"></typeparam>
    /// <exception cref="Exception"></exception>
    public static IResourceBuilder<CatalystProject> WithComponent<MetadataType>(
        this IResourceBuilder<CatalystProject> catalystProject,
        string name,
        CatalystComponent<MetadataType> component
    )
    {
        var metadataSerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        var serialized = JsonSerializer.Serialize(component.Metadata, metadataSerializerOptions);
        var metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(serialized, metadataSerializerOptions)
            ?? throw new("Failed to prepare component metadata.");

        catalystProject.Resource.Components.Add(name, new()
        {
            Name = name,
            Type = component.Type,
            Scopes = component.Scopes,
            Metadata = metadata,
        });

        return catalystProject;
    }

    /// <summary>
    ///     Adds a sample Catalyst-managed pubsub to the Catalyst project. The Diagrid CLI creates its fronting
    ///     component; this does not add one.
    ///     <br /><br />
    ///     This overload is useful for demos and quick prototyping, it's recommended to use the full overload for your projects.
    ///     <br /><br />
    ///     See: <see cref="WithCatalystPubSub(IResourceBuilder{CatalystProject}, string, IList{string}?)"/> 
    /// </summary>
    /// <param name="catalystProject"></param>
    /// <returns></returns>
    public static IResourceBuilder<CatalystProject> WithCatalystPubSub(this IResourceBuilder<CatalystProject> catalystProject)
    {
        // `pubsub create` creates the fronting component itself; registering one here would conflict.
        return catalystProject.WithCatalystPubSub(SamplePubsubName);
    }
    
    /// <summary>
    ///     Adds a Catalyst-managed PubSub to the Catalyst project.
    ///     <br /><br />
    ///     Please note that does not add a component to the project. 
    /// </summary>
    /// <param name="catalystProject"></param>
    /// <param name="name"></param>
    /// <param name="scopes"></param>
    public static IResourceBuilder<CatalystProject> WithCatalystPubSub(
        this IResourceBuilder<CatalystProject> catalystProject,
        string name,
        IList<string>? scopes = null
    )
    {
        if (
        catalystProject.Resource.PubSubs.ContainsKey(name)
        || catalystProject.Resource.KvStores.ContainsKey(name)
        )
            throw new("Catalyst service names must be unique.");
        
        var pubSub = new PubSubDescriptor
        {
            Project = catalystProject.Resource.ProjectName,
            Scopes = scopes ?? [],
        };

        catalystProject.Resource.PubSubs.Add(name, pubSub);

        return catalystProject;
    }

    /// <summary>
    ///     Adds a sample Catalyst-managed KV store to the Catalyst project. The Diagrid CLI creates its fronting
    ///     component; this does not add one, and it does not set actorStateStore.
    ///     <br /><br />
    ///     This overload is useful for demos and quick prototyping, it's recommended to use the full overload for your projects.
    ///     <br /><br />
    ///     See: <see cref="WithCatalystKvStore(IResourceBuilder{CatalystProject}, string, IList{string}?)"/>
    /// </summary>
    /// <param name="catalystProject"></param>
    /// <returns></returns>
    public static IResourceBuilder<CatalystProject> WithCatalystKvStore(this IResourceBuilder<CatalystProject> catalystProject)
    {
        // `kv create` creates the fronting component itself; registering one here would conflict. It does not
        // claim actorStateStore either (the managed workflow state store does); set it explicitly through
        // DiagridStateStore.ActorStateStore if needed.
        return catalystProject.WithCatalystKvStore(SampleKvName);
    }
    
    /// <summary>
    ///     Adds a Catalyst-managed KV store to the Catalyst project.
    ///     <br /><br />
    ///     Please note that does not add a component to the project.
    /// </summary>
    /// <param name="catalystProject"></param>
    /// <param name="name"></param>
    /// <param name="scopes"></param>
    public static IResourceBuilder<CatalystProject> WithCatalystKvStore(
        this IResourceBuilder<CatalystProject> catalystProject,
        string name,
        IList<string>? scopes = null
    )
    {
        if (
            catalystProject.Resource.PubSubs.ContainsKey(name)
            || catalystProject.Resource.KvStores.ContainsKey(name)
        )
            throw new("Catalyst service names must be unique.");
        
        var kvStore = new KvStoreDescriptor
        {
            Project = catalystProject.Resource.ProjectName,
            Scopes = scopes ?? [],
        };

        catalystProject.Resource.KvStores.Add(name, kvStore);

        return catalystProject;
    }
    
    /// <summary>
    ///     Ensures the user has configured a Catalyst project as part of their orchestration.
    /// </summary>
    /// <param name="applicationBuilder"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    internal static IList<CatalystProject> EnsureCatalystResources(this IDistributedApplicationBuilder applicationBuilder)
    {
        var catalystProjectResources = applicationBuilder.Resources
            .Where((resource) => resource is CatalystProject)
            .Cast<CatalystProject>()
            .ToList();
        
        if (! catalystProjectResources.Any())
            throw new($"Remember to configure your Catalyst project by calling {nameof(AddCatalystProject)}.");
        
        return catalystProjectResources;
    }
}
