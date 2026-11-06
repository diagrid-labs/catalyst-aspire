using Aspire.Hosting;
using Diagrid.Aspire.Test.AppHost;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// USE_CATALYST=false runs Dapr OSS (CommunityToolkit); true runs Diagrid. TestApi is identical either way.
DemoWiring.Configure<TestApi>(builder, DemoWiring.UseCatalyst(builder));

builder.Build().Run();
