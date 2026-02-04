using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.Aspire.Hosting.Catalyst;

public static class ResourceBuilderExtensions
{
    /// <summary>
    ///     Associates an Aspire orchestration to a Catalyst project.
    /// </summary>
    /// <param name="applicationBuilder"></param>
    /// <param name="customProjectName"></param>
    public static IResourceBuilder<CatalystProject> AddCatalystProject(this IDistributedApplicationBuilder applicationBuilder, string? customProjectName = null)
    {
        applicationBuilder.Services.AddSingleton<CatalystProvisioner, CliCatalystProvisioner>();

        // todo: Either replace `"aspire"` here with a default inferred from `applicationBuilder`, or make `projectName` a required param.
        var projectName = customProjectName ?? "aspire";

        // todo: Custom icon, support pending https://github.com/dotnet/aspire/issues/8684
        var catalystProject = applicationBuilder.AddResource(new CatalystProject
        {
            ProjectName = projectName,
        });

        catalystProject.WithAnnotation(new ResourceUrlAnnotation
        {
            Url = "https://catalyst.r1.diagrid.io/admin/organization",
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

    /// <summary>
    ///     Configures a project to use Catalyst.
    /// </summary>
    /// <param name="resourceBuilder"></param>
    /// <param name="catalystProjectBuilder"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static IResourceBuilder<ResourceType> WithCatalyst<ResourceType>(this IResourceBuilder<ResourceType> resourceBuilder, IResourceBuilder<CatalystProject>? catalystProjectBuilder = null)
    where ResourceType : Resource, IResourceWithEnvironment, IResourceWithWaitSupport
    {
        var applicationBuilder = resourceBuilder.ApplicationBuilder;
        var catalystProjects = applicationBuilder.EnsureCatalystResources();

        if (catalystProjects.Count > 1 && catalystProjectBuilder is null)
            throw new($"Your Aspire orchestration has multiple Catalyst projects configured. Please specify which one to use when calling {nameof(WithCatalyst)}.");
            
        catalystProjectBuilder ??= applicationBuilder.CreateResourceBuilder(catalystProjects.Single());

        resourceBuilder.WaitForCompletion(catalystProjectBuilder);
        
        if (
            resourceBuilder.Resource is IResourceWithEndpoints resourceWithEndpoints
            && resourceWithEndpoints.GetEndpoints().FirstOrDefault((endpoint) => endpoint.IsHttp) is {} httpEndpoint
        )
        {
            var catalystProxy = applicationBuilder
                .AddExecutable(
                    $"{resourceBuilder.Resource.Name}-catalyst-proxy",
                    "diagrid",
                    // todo: I want a better, more explicit PWD than this.
                    ".",
                    [
                        "dev", "run",
                        "--approve",
                        "--project", catalystProjectBuilder.Resource.ProjectName,
                        "--app-id", resourceBuilder.Resource.Name,
                        "--app-port", httpEndpoint.Property(EndpointProperty.Port),
                        "--skip-managed-kv",
                        "--skip-managed-pubsub",
                    ]
                )
                .WaitForCompletion(catalystProjectBuilder);

            resourceBuilder
                .WaitFor(catalystProxy)
                .WithChildRelationship(catalystProxy);

            catalystProjectBuilder.Resource.AppDetails[resourceBuilder.Resource] = new();
        }

        resourceBuilder.WithEnvironment(async (context) =>
        {
            var applicationModel = (DistributedApplicationModel) context.ExecutionContext.ServiceProvider
                .GetRequiredService(typeof(DistributedApplicationModel));

            var catalystProject = applicationModel.Resources.FirstOrDefault((resource) => resource is CatalystProject) as CatalystProject
                ?? throw new("This project is missing a Catalyst project resource.");

            var appDetails = await catalystProject.AppDetails[resourceBuilder.Resource].Task;

            context.EnvironmentVariables["DAPR_GRPC_ENDPOINT"] = await catalystProject.GrpcEndpoint.Task;
            context.EnvironmentVariables["DAPR_HTTP_ENDPOINT"] = await catalystProject.HttpEndpoint.Task;
            context.EnvironmentVariables["DAPR_API_TOKEN"] = appDetails.ApiToken;
        });

        return resourceBuilder;
    }

    /// <summary>
    ///     Adds a weakly-typed component to the Catalyst project.
    /// </summary>
    /// <param name="catalystProject"></param>
    /// <param name="name"></param>
    /// <param name="type"></param>
    /// <param name="metadata"></param>
    /// <param name="scopes"></param>
    public static void WithComponent(
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
    ///     Adds a Catalyst-managed PubSub to the Catalyst project.
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
    ///     Adds a Catalyst-managed KV store to the Catalyst project.
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
