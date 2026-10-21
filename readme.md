# Diagrid Catalyst Aspire Integration

![NuGet Version](https://img.shields.io/nuget/v/Diagrid.Aspire.Hosting.Catalyst)

This integration allows you to seamlessly connect your locally running Aspire projects to live Diagrid Catalyst infrastructure.

## What changed in this release

If you are upgrading an existing AppHost, read the first three items before you run it. They change behavior without any compile error, and the first two affect live data and message delivery.

### Upgrade hazards

1. **Duplicate topic delivery until you delete the old subscription.** Generated subscription names changed from `{app}-pubsub` to `{app}-pubsub-{topic}`. The old scheme crashed as soon as an app subscribed to a second topic, so this was necessary. The consequence for an existing project: the first run creates the new subscription and leaves the old `{app}-pubsub` one in place, so **the topic is delivered twice** to your app. This looks like a bug in your app, not an incomplete upgrade. Delete the old subscription once per project and app:

   ```bash
   diagrid subscription delete your-app-pubsub --project your-project
   ```

   You can also delete it in the Catalyst portal. Subscriptions you named yourself through `WithSubscription(name, ...)` are not affected.

2. **`WithCatalystKvStore()` now creates `statestore`, not `kvstore`, and the old store is orphaned.** The default name changed so that Dapr OSS samples (which use `statestore`) work through the `USE_CATALYST` switch without editing service code. An existing AppHost creates a **new, empty** `statestore` on first run. The old `kvstore` and its data are left behind, not migrated and not read. To keep using your data, pin the old name explicitly: `WithCatalystKvStore("kvstore")`.

3. **Scopes and metadata are reset on every run.** Components and subscriptions now reconcile: each run compares the AppHost's declaration with Catalyst and updates the resource when they differ, resending the declared values wholesale. The AppHost is the source of truth. Scopes or metadata that you set in the portal or with the CLI are **overwritten** on the next run. Put them in the AppHost.

### What reconciles and what does not

| Resource | On re-run |
| --- | --- |
| Components | Reconciled (updated to match the AppHost) |
| Subscriptions | Reconciled |
| Project | Create-only |
| App | Create-only |
| Agent | Create-only |
| KV store | Create-only |
| Pub/sub | Create-only |

For create-only resources, a change you make in the AppHost is **ignored** if the resource already exists, and the run still succeeds. To change a region, a body size or another project setting, delete the resource and let the AppHost create it again.

`EnableWorkflowHistorySigning` is the one to watch. It can only be chosen when the project is created and cannot be changed afterwards, so setting it against an existing project silently does nothing.

### Smaller changes

- **`WithCatalystApp()` is the canonical name** for turning a resource into a Catalyst app. `WithCatalyst()` still works, is not marked `[Obsolete]`, and behaves identically.
- **`WithCatalystAgent()`** runs a resource as a Catalyst agent (`diagrid agent create`) instead of an app. Support depends on the Catalyst deployment; when it has no agent API, provisioning fails naming the resource.
- **New `ProjectDescriptor` options:** `Region`, `RegionGroup`, `DeployManagedKv`, `DeployManagedPubsub`, `GlobalIdMaxBodySize`, `EnableWorkflowHistorySigning` and `DisableAppTunnels`. Previously there was no way to choose a region at all. Apart from `DisableAppTunnels`, they apply only when the project is created (see above). `Region` and `RegionGroup` are mutually exclusive.

  ```csharp
  builder.AddCatalystProject("your-project", new ProjectDescriptor
  {
      Region = "your-region",
      EnableManagedWorkflow = true,
      GlobalIdMaxBodySize = "8Mi",
  });
  ```

- **Managed services also create resiliency policies.** `DeployManagedKv` (`--deploy-managed-kv`) creates a `managed-kvstore-policy` resiliency policy, and `DeployManagedPubsub` (`--deploy-managed-pubsub`) creates `managed-pubsub-policy`.
- **Bulk subscriptions.** The longer `WithSubscription(name, component, topic, route, bulk, bulkMaxMessagesCount, bulkMaxAwaitDuration)` overload sets the bulk options. Pass `null` to use the Catalyst default.
- **The dashboard link follows `DIAGRID_URL`.** It used to be hardcoded to `catalyst.r1.diagrid.io`. It is now derived from the same `DIAGRID_URL` variable and rules as the Diagrid CLI, so for the default API URL it points at `catalyst.diagrid.io`.
- **The `diagrid dev run` proxy starts in the AppHost's directory** instead of `.`.
- **`AddCatalystProject()` with no name** derives one from the entry assembly's namespace. When it cannot, it now throws an `ArgumentException` naming `customProjectName` instead of silently using `aspire`.
- **`WithCatalystKvStore()` and `WithCatalystPubSub()` do not add a component.** The CLI creates the fronting component, named after the store. Older XML docs said otherwise.

### `actorStateStore`

The convenience `WithCatalystKvStore()` no longer sets `actorStateStore`. With `EnableManagedWorkflow = true`, Catalyst already provisions a `workflows-state` store carrying that flag, and the sidecar picks the first in-scope flagged store by map iteration order. A second flagged store would make that choice arbitrary. If you want your own store to hold actor state, opt in explicitly through `DiagridStateStore`:

```csharp
builder
    .AddCatalystProject("your-project")
    .WithCatalystKvStore("your-kv")
    .WithComponent("your-actor-state", new DiagridStateStore
    {
        Scopes = ["your-app"],
        Metadata = new()
        {
            State = "your-kv",
            ActorStateStore = true,
        },
    });
```

With `EnableManagedWorkflow` also on, two flagged stores are then in scope, so check which one your workflows end up using.

### Demo note

In the `AppHost` demo, the OSS side of the `USE_CATALYST` switch keeps workflow state and order state in one **in-memory** store. Workflows are **not durable across a sidecar restart there**. The crash-and-resume demonstration only works on the Catalyst side.

### Breaking API changes

`CatalystProvisioner` is the swappable abstraction behind the CLI calls, and it changed in several places. If you implement or subclass it, you are affected:

- `CreateApp` and `GetAppDetails` gained a `projectName` parameter.
- `GetProjectDetails` is replaced by `TryGetProjectDetails` and `WaitForProjectReady`.
- `Init` gained an `ILogger` parameter.
- `CheckKvStoreExists` is replaced by `KvStoreExists` and `PubSubExists`.
- `CreateComponent` is now `UpsertComponent`.
- `CreateSubscription` is now `UpsertSubscription`.
- `CreateAgent` and `GetAgentDetails` are new.
- `CreateAppOptions` is now `CreateIdentityOptions`. Properties were renamed (for example `AppEndpoint` to `Endpoint`) and `Project` is now required.
- `CliAppDetails` and five CLI output DTOs were removed.
- The `ResolveApiToken` signature changed.
- `IgnoreIfExists` now defaults to `true` on four options records.
- The **value** of the `SampleKvName` constant changed to `statestore`. C# inlines constant values at compile time, so anything compiled against the old package keeps using `kvstore` until you **recompile** it.

## Getting Started

### 1 - Get a Catalyst Account

[Sign up for a Diagrid Catalyst account.](https://www.diagrid.io/catalyst)

### 2 - Install the Diagrid CLI

This project requires that you have the [Diagrid CLI](https://docs.diagrid.io/references/catalyst/cli-reference/intro) installed.

After installing the CLI, please be sure to [log in to your Diagrid account](https://docs.diagrid.io/references/catalyst/cli-reference/intro/#using-the-cli) by running:

```bash
diagrid login
```

### 3 - Add the integration to your AppHost project

You can find [the package on NuGet](https://www.nuget.org/packages/Diagrid.Aspire.Hosting.Catalyst).

Add the package by running:

```bash
dotnet add package Diagrid.Aspire.Hosting.Catalyst
```

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
    .WithCatalystApp();
```

Each resource that you tag in this way will automatically have an application entry created in the Catalyst UI. Use `WithCatalystAgent()` instead to register the resource as a Catalyst agent. `WithCatalyst()` is an equivalent alias of `WithCatalystApp()`.

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

Creating a managed KV store or pub/sub through `WithCatalystKvStore` or `WithCatalystPubSub` also creates a component with the same name, so you do not need `WithComponent` for the basic case. Your services can use `your-desired-kv-name` as their state store name straight away. Do not register a component under the same name as the service; that conflicts with the one the CLI creates.

Use `WithComponent` for additional or customized components, for example a second component over the same KV store with a key prefix and an explicit scope:

```csharp
builder
    .AddCatalystProject("your-desired-project-name")
    .WithCatalystKvStore("your-desired-kv-name")
    .WithComponent("your-desired-kv-component", new DiagridStateStore
    {
        Scopes = ["my-project"],
        Metadata = new()
        {
            State = "your-desired-kv-name",
            KeyPrefix = "orders",
        },
    });
```

Components declared this way reconcile on every run (see [What changed in this release](#what-changed-in-this-release)).

#### Custom Components

> Work is ongoing to get all supported components added!

If you need a component that does not have strong typing yet, you can always fall back on the `IDictionary<string, object>` overload of `WithComponent`:

```csharp
.WithComponent(
    "diagrid-homepage", 
    "bindings.http", 
    new Dictionary<string, object?>
    {
        ["url"] = "https://diagrid.io",
    },
    [ "test-api" ]
)
```

The example above will create an [http binding](https://docs.diagrid.io/references/catalyst/components-reference/bindings/http) with the `url` property set to `https://diagrid.io`.  The final array parameter with `["test-api"]` is where you can provide which appids to scope the component to.


## Additional Resources

See an example of this integration being used in the [Diagrid Catalyst Workflows demo](https://github.com/diagrid-labs/catalyst-order-workflow-dotnet).
