using System.Collections.Generic;
using System.Threading.Tasks;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Diagrid.Aspire.Hosting.Catalyst;

public class CatalystProject : IResource
{
    /// <summary>
    ///     The label of the resource in Aspire. See ProjectName for the label that will be used in Catalyst.
    /// </summary>
    public string Name => $"catalyst-{ProjectName}";
    public ResourceAnnotationCollection Annotations { get; init; } = [];

    /// <summary>
    ///     The label of the project in Catalyst. See Name for the label that will be used in Aspire.
    /// </summary>
    public required string ProjectName { get; init; }
    
    internal TaskCompletionSource<string> HttpEndpoint { get; } = new();
    internal TaskCompletionSource<string> GrpcEndpoint { get; } = new();

    internal Dictionary<Resource, TaskCompletionSource<AppDetails>> AppDetails { get; init; } = new();

    internal Dictionary<string, PubSubDescriptor> PubSubs { get; init; } = new();
    internal Dictionary<string, KvStoreDescriptor> KvStores { get; init; } = new();
    internal Dictionary<string, ComponentDescriptor> Components { get; init; } = new();
    internal Dictionary<string, SubscriptionDescriptor> Subscriptions { get; init; } = new();
}
