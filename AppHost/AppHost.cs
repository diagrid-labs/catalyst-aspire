using System.Collections.Generic;
using Aspire.Hosting;
using Diagrid.Aspire.Hosting.Catalyst;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var catalystProjectOne = builder
    .AddCatalystProject()
    .WithCatalystKvStore()
    .WithCatalystPubSub()
    .WithComponent(
        "diagrid-homepage", 
        "bindings.http", 
        new Dictionary<string, object?>
        {
            ["url"] = "https://diagrid.io",
        },
        [ "test-api" ]
    );

builder
    .AddProject<TestApi>("test-api")
    .WithCatalyst(catalystProjectOne)
    .WithSubscription("test", "/");

builder.Build().Run();
