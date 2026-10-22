using System.IO;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Diagrid.Aspire.Hosting.Catalyst;
using Microsoft.Extensions.Configuration;

namespace Diagrid.Aspire.Test.AppHost;

/// <summary>
///     The one place the USE_CATALYST switch lives. AppHost.cs calls it, and so do the tests, so the shape the
///     tests assert is the shape that runs. Both sides address components named "statestore" and "pubsub" —
///     the Catalyst side is told the state store name explicitly, because the Catalyst default is "kvstore".
/// </summary>
public static class DemoWiring
{
    public const string DefaultProjectName = "catalyst-aspire-demo";

    public const string SubscriptionRoute = "/orders/received";

    /// <summary>
    ///     The state store component name both sides of the switch use. It is the Dapr OSS name (see
    ///     Resources/statestore.yaml) and TestApi addresses it directly, so the Catalyst side must be told to
    ///     use it too: that is the one line a developer moving from Dapr OSS has to write.
    /// </summary>
    public const string StateStoreName = "statestore";

    public static IResourceBuilder<ProjectResource> Configure<TProject>(IDistributedApplicationBuilder builder, bool useCatalyst, string projectName = DefaultProjectName)
        where TProject : IProjectMetadata, new()
    {
        var api = builder.AddProject<TProject>("test-api");

        if (useCatalyst)
        {
            // The store is named explicitly, not left to the default: its name must match the Dapr state store
            // the services already address ("statestore", from Resources/statestore.yaml), and that is the normal
            // case rather than the exception. The Catalyst default is "kvstore", which is what the resource is
            // called in Catalyst, so switching from Dapr OSS means naming it rather than accepting the default.
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
