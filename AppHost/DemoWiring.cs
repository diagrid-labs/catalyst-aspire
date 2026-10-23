using System.IO;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Diagrid.Aspire.Hosting.Catalyst;
using Microsoft.Extensions.Configuration;

namespace Diagrid.Aspire.Test.AppHost;

/// <summary>
///     The one place the USE_CATALYST switch lives. AppHost.cs calls it, and so do the tests, so the shape the
///     tests assert is the shape that runs. Both sides address components named "kvstore" and "pubsub".
/// </summary>
public static class DemoWiring
{
    public const string DefaultProjectName = "catalyst-aspire-demo";

    public const string SubscriptionRoute = "/orders/received";

    /// <summary>
    ///     The state store component name both sides of the switch use, defined as Catalyst's own default so the
    ///     two cannot drift. The Dapr OSS side is named to match (see Resources/kvstore.yaml) rather than the
    ///     other way round, because the default is not arbitrary: creating a project's first agent provisions a
    ///     managed KV store called <c>kvstore</c>, and an organization capped at one KV store per project cannot
    ///     then hold a second under a different name. Matching the default keeps the demo inside that cap and
    ///     still leaves the service code identical on both sides.
    /// </summary>
    public const string StateStoreName = Diagrid.Aspire.Hosting.Catalyst.ResourceBuilderExtensions.SampleKvName;

    public static IResourceBuilder<ProjectResource> Configure<TProject>(IDistributedApplicationBuilder builder, bool useCatalyst, string projectName = DefaultProjectName)
        where TProject : IProjectMetadata, new()
    {
        var api = builder.AddProject<TProject>("test-api");

        if (useCatalyst)
        {
            // The store is still named explicitly even though StateStoreName is the default: switching from
            // Dapr OSS normally means naming the store after the component your services already address, and
            // the demo should show that line rather than hide it behind a default that happens to agree.
            var catalyst = builder.AddCatalystProject(projectName, new() { EnableManagedWorkflow = true })
                .WithCatalystKvStore(StateStoreName)
                .WithCatalystPubSub();

            api.WithCatalystApp(catalyst).WithSubscription("orders", SubscriptionRoute);
        }
        else
        {
            builder.AddDapr();
            api.WithDaprSidecar(new DaprSidecarOptions
            {
                ResourcesPaths = [Path.Combine(builder.AppHostDirectory, "Resources")],
            });
        }

        return api;
    }

    public static bool UseCatalyst(IDistributedApplicationBuilder builder) => builder.Configuration.GetValue<bool>("USE_CATALYST");
}
