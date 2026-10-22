using Aspire.Hosting;
using Diagrid.Aspire.Test.AppHost;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// USE_CATALYST=false runs Dapr OSS (CommunityToolkit); true runs Diagrid Catalyst. TestApi is identical either way.
DemoWiring.Configure<TestApi>(builder, DemoWiring.UseCatalystAsync(builder));

builder.Build().Run();
