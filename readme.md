# Diagrid Catalyst Aspire

![NuGet Version](https://img.shields.io/nuget/v/Diagrid.Aspire.Hosting.Catalyst)

This integration allows you to seamlessly connect your locally running Aspire based projects to live Diagrid Catalyst infrastructure.

## Getting Started

### Get a Catalyst Account

[Sign up for a Diagrid Catalyst account.](https://diagrid.ws/get-catalyst)

> You can skip this step if you already have one. 🙂

### Install the Diagrid CLI

This project requires that you have the [Diagrid CLI](https://docs.diagrid.io/references/catalyst/cli-reference/intro) installed.

After installing the CLI, please be sure to [log in to your Diagrid account](https://docs.diagrid.io/references/catalyst/cli-reference/intro/#using-the-cli) by running:

```bash
diagrid login
```

### Add the integration to your AppHost project

You can find [the package on NuGet](https://www.nuget.org/packages/Diagrid.Aspire.Hosting.Catalyst).

### Configure your AppHost

At a minimum, you must always add the following to your project to enable the integration: 

```csharp
builder.AddCatalystProject("your-desired-project-name");
```

This will ensure that a Catalyst project exists or that you are connected to an existing one.

After that, you can start defining your [Catalyst applications](https://docs.diagrid.io/catalyst/connect) by annotating 
your project resources and containers:

```csharp
builder
    .AddProject<MyProject>("my-project")
    .WithCatalyst();
```

Each resource that you tag in this way will automatically have an application entry created in the Catalyst UI.

## Configuring Services and Components

Outside your Catalyst project and applications, you are also going to want to configure components and optionally services.

### Services

Catalyst offers easy to use managed services that can be used for state storage and pubsub messaging components.

To ensure these services are available in your project, add the following when configuring your project:

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
