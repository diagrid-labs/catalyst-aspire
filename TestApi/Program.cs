using Diagrid.Aspire.Test.ServiceDefaults;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
