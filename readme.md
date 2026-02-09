# Diagrid Catalyst Aspire Integration

![NuGet Version](https://img.shields.io/nuget/v/Diagrid.Aspire.Hosting.Catalyst)

This integration allows you to seamlessly connect your locally running Aspire projects to live Diagrid Catalyst infrastructure.

## Getting Started

### 1 - Get a Catalyst Account

[Sign up for a Diagrid Catalyst account.](https://www.diagrid.io/catalyst)

### 2 - Install the Diagrid CLI

This project requires that you have the [Diagrid CLI](https://docs.diagrid.io/references/catalyst/cli-reference/intro) installed.

After installing the CLI, please be sure to [log in to your Diagrid account](https://docs.diagrid.io/references/catalyst/cli-reference/intro/#using-the-cli) by running:

```bash
diagrid login
```

### 3-  Add the integration to your AppHost project

You can find [the package on NuGet](https://www.nuget.org/packages/Diagrid.Aspire.Hosting.Catalyst).

### 4 - Configure your AppHost

At a minimum, you must always add the following to your project to enable the integration: 

```csharp
builder.AddCatalystProject("your-desired-project-name");
```

This will ensure that a Catalyst project exists or that you are connected to an existing one.

After that, you can start defining your [Catalyst appids](https://docs.diagrid.io/catalyst/connect) by annotating 
your project resources and containers:

```csharp
builder
    .AddProject<MyProject>("my-project")
    .WithCatalyst();
```

Each resource that you tag in this way will automatically have an application entry created in the Catalyst UI.

## 5 - Configuring Managed Services and Components

Outside your Catalyst project and appids, you are also going to want to configure components and optionally managed services.

### Managed Services

Catalyst offers easy-to-use managed services for state storage and pubsub messaging components.

To ensure these services are available in your project, add the following to your AppHost:

```csharp
builder
    .AddCatalystProject("your-desired-project-name")
    .WithCatalystKvStore("your-desired-kv-name")
    .WithCatalystPubSub("your-desired-pubsub-name");
```

### Components

Catalyst supports [a variety of components](https://docs.diagrid.io/references/components-reference/intro) that you can use
when building your applications.

For example, to add a Catalyst backed state store component, you'll want to create both the service and then the component:

```csharp
builder
    .AddCatalystProject("your-desired-project-name")
    .WithCatalystKvStore("your-desired-kv-name")
    .WithComponent("your-desired-kv-component", new DiagridStateStore
    {
        Metadata = new()
        {
            State = "your-desired-kv-name",
        },
    });
```

> We are working on getting all supported components added.  If you need a component that is not listed, you can fall back on the `IDictionary<string, object>` overload of `WithComponent`.

## Additional Resources

See an example of this integration being used in the [Diagrid Catalyst Workflows demo](https://github.com/diagrid-labs/catalyst-order-workflow-dotnet).
