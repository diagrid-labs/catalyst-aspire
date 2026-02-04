using System.Collections.Generic;
using System.Threading.Tasks;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Diagrid.Aspire.Hosting.Catalyst;

public class CatalystProject : IResource
{
    public string Name => $"catalyst-{ProjectName}";
    public required string ProjectName { get; init; }
    public ResourceAnnotationCollection Annotations { get; init; } = [];

    internal TaskCompletionSource<string> HttpEndpoint { get; } = new();
    internal TaskCompletionSource<string> GrpcEndpoint { get; } = new();

    internal Dictionary<Resource, TaskCompletionSource<AppDetails>> AppDetails { get; init; } = new();

    internal Dictionary<string, PubSubDescriptor> PubSubs { get; init; } = new();
    internal Dictionary<string, KvStoreDescriptor> KvStores { get; init; } = new();
    internal Dictionary<string, ComponentDescriptor> Components { get; init; } = new();
}
