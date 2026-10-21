using System.IO;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Diagrid.Aspire.Hosting.Catalyst;
using Microsoft.Extensions.Configuration;

namespace Diagrid.Aspire.Test.AppHost;

/// <summary>
///     The one place the USE_CATALYST switch lives. AppHost.cs calls it, and so do the tests, so the shape the
///     tests assert is the shape that runs. Both sides address components named "statestore" and "pubsub".
/// </summary>
public static class DemoWiring
{
    public const string SubscriptionRoute = "/orders/received";

    public static IResourceBuilder<ProjectResource> Configure<TProject>(IDistributedApplicationBuilder builder, bool useCatalyst)
        where TProject : IProjectMetadata, new()
    {
        var api = builder.AddProject<TProject>("test-api");

        if (useCatalyst)
        {
            var catalyst = builder.AddCatalystProject("catalyst-aspire-demo", new() { EnableManagedWorkflow = true })
                .WithCatalystKvStore()
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
