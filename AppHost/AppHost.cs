using Aspire.Hosting;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.ComponentSpec;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var catalystProjectOne = builder
    .AddCatalystProject("aspire-test")
    .WithCatalystKvStore("test-kv")
    .WithCatalystPubSub("test-pubsub")
    .WithComponent("test-pubsub", new DiagridPubSub
    {
        Metadata = new()
        {
            PubSubName = "test-pubsub",
        },
    })
    .WithComponent("test-kv", new DiagridStateStore
    {
        Metadata = new()
        {
            State = "test-kv",
        },
    });

builder
    .AddProject<TestApi>("test-api")
    .WithCatalyst(catalystProjectOne)
    .WithSubscription("test-subscription", "test-pubsub", "test", "/");

builder.Build().Run();
