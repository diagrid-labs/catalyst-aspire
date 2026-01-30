using Aspire.Hosting;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.ComponentSpec;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder
    .AddCatalystProject("catalyst-aspire-test")
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
    .WithCatalyst();

builder.Build().Run();
