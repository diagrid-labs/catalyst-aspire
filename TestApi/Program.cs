using System.Text.Json;
using System.Threading.Tasks;
using Dapr;
using Dapr.Client;
using Dapr.Workflow;
using Diagrid.Aspire.Test.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// Component names are the contract with the AppHost: Dapr OSS and Catalyst both expose "statestore" and "pubsub".
const string StateStore = "statestore";
const string PubSub = "pubsub";
const string Topic = "orders";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDaprClient();
builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<ProcessOrderWorkflow>();
    options.RegisterActivity<ReserveStockActivity>();
    options.RegisterActivity<ShipOrderActivity>();
});

var app = builder.Build();

app.UseCloudEvents();
app.MapSubscribeHandler();

app.MapGet("/", () => "Hello World!");

app.MapPost("/orders", async (Order order, DaprClient dapr) =>
{
    await dapr.PublishEventAsync(PubSub, Topic, order);
    return Results.Accepted();
});

// Subscriber route. On Catalyst the AppHost declares the subscription; on Dapr OSS /dapr/subscribe serves it.
app.MapPost("/orders/received", async (Order order, DaprClient dapr) =>
{
    await dapr.SaveStateAsync(StateStore, order.Id, order);
    return Results.Ok();
}).WithTopic(PubSub, Topic);

app.MapGet("/orders/{id}", async (string id, DaprClient dapr) =>
{
    var order = await dapr.GetStateAsync<Order?>(StateStore, id);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapPost("/orders/{id}/process", async (string id, DaprWorkflowClient workflows) =>
{
    var instanceId = await workflows.ScheduleNewWorkflowAsync(nameof(ProcessOrderWorkflow), input: id);
    return Results.Accepted($"/orders/{id}", new { instanceId });
});

app.Run();

public sealed record Order(string Id, string Item);

public sealed class ProcessOrderWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string orderId)
    {
        await context.CallActivityAsync<bool>(nameof(ReserveStockActivity), orderId);
        return await context.CallActivityAsync<string>(nameof(ShipOrderActivity), orderId);
    }
}

public sealed class ReserveStockActivity : WorkflowActivity<string, bool>
{
    public override Task<bool> RunAsync(WorkflowActivityContext context, string orderId) => Task.FromResult(true);
}

public sealed class ShipOrderActivity : WorkflowActivity<string, string>
{
    public override Task<string> RunAsync(WorkflowActivityContext context, string orderId) => Task.FromResult($"shipped:{orderId}");
}
