# Diagrid Catalyst Aspire Integration

![NuGet Version](https://img.shields.io/nuget/v/Diagrid.Aspire.Hosting.Catalyst)

This integration allows you to seamlessly connect your locally running Aspire projects to live Diagrid Catalyst infrastructure.

## What this is

.NET Aspire's AppHost describes the pieces of your application — projects, containers, the resources they depend on — and runs them together on your machine. This package extends that model to [Diagrid Catalyst](https://www.diagrid.io/catalyst): declare a Catalyst project, its apps and agents, managed KV stores and pub/sub brokers, components and subscriptions in the AppHost, and the integration provisions them through the Diagrid CLI every time you run.

Your services keep talking to a Dapr sidecar the way they always did. Each resource you annotate gets a companion proxy resource in the Aspire model that connects it to Catalyst, so **moving a Dapr OSS service to Catalyst needs no change to the service's own code** — the switch happens in the AppHost.

What you get from the AppHost:

- A Catalyst **project**, created if it does not exist, or connected to if it does.
- An **app** or **agent** registration per annotated resource.
- **Managed KV store and pub/sub** services, with the components that front them.
- **Components and subscriptions**, reconciled against your declarations on every run.

### Requirements

- A [Diagrid Catalyst account](https://www.diagrid.io/catalyst).
- The [Diagrid CLI](https://docs.diagrid.io/references/catalyst/cli-reference/intro), **1.85.0 or newer**. Provisioning checks the installed version up front and fails if it is older. If the version cannot be read, the check is skipped with a warning.
- .NET 8, 9 or 10.

> **Upgrading from 1.x?** Read [Upgrading from 1.x to 2.0](#upgrading-from-1x-to-20) first. Three of the changes alter behavior without producing a compile error, and one of them affects live message delivery.

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

**Pass a project name.** `AddCatalystProject()` with no name throws for AppHosts that use top-level statements, which is the default Aspire template: their entry point is `Program` in the global namespace, so there is no namespace to derive a name from.

After that, you can start defining your [Catalyst appids](https://docs.diagrid.io/catalyst/connect) by annotating
your project resources and containers:

```csharp
builder
    .AddProject<MyProject>("my-project")
    .WithCatalystApp();
```

Each resource that you tag in this way will automatically have an application entry created in the Catalyst UI. Use `WithCatalystAgent()` instead to register the resource as a Catalyst agent — support for that depends on the Catalyst deployment, and where there is no agent API, provisioning fails naming the resource. `WithCatalyst()` is an equivalent alias of `WithCatalystApp()`.

Resources with no HTTP endpoint, such as workflow-only workers, are supported.

**Resource names are validated.** A name that is not a valid Catalyst name is rejected, as is one whose derived `{name}-catalyst-proxy` resource would exceed Aspire's 64-character limit — so names longer than 49 characters.

The link to the Catalyst dashboard is derived from the `DIAGRID_URL` variable, using the same rules as the Diagrid CLI. For the default API URL it points at `catalyst.diagrid.io`.

#### Project options

`AddCatalystProject` takes an optional `ProjectDescriptor` that controls how the project is created:

```csharp
builder.AddCatalystProject("your-project", new ProjectDescriptor
{
    Region = "your-region",
    EnableManagedWorkflow = true,
    GlobalIdMaxBodySize = "8Mi",
});
```

| Option | Effect |
| --- | --- |
| `Region` / `RegionGroup` | Where the project is created. Mutually exclusive. |
| `DeployManagedKv` | Creates a managed KV store, plus a `managed-kvstore-policy` resiliency policy. |
| `DeployManagedPubsub` | Creates a managed pub/sub, plus a `managed-pubsub-policy` resiliency policy. |
| `EnableManagedWorkflow` | Provisions Catalyst's managed workflow infrastructure, including a `workflows-state` store. |
| `GlobalIdMaxBodySize` | Maximum request body size. |
| `EnableWorkflowHistorySigning` | Signs workflow history. Can only be chosen at project creation. |
| `DisableAppTunnels` | Turns off app tunnels. |

**All of these apply only when the project is created.** This integration never runs `diagrid project update`, so against an existing project they are silently ignored — see [How provisioning behaves on re-run](#how-provisioning-behaves-on-re-run).

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

With no name, `WithCatalystKvStore()` creates a store called `kvstore`. Pass a name when the store has to match a Dapr state store component your services already address — that is the one line that switching from Dapr OSS costs.

### Components

Catalyst supports [a variety of components](https://docs.diagrid.io/references/components-reference/intro) that you can use
when building your applications.

Creating a managed KV store or pub/sub through `WithCatalystKvStore` or `WithCatalystPubSub` also creates a component with the same name, so you do not need `WithComponent` for the basic case. Your services can use `your-desired-kv-name` as their state store name straight away. Do not register a component under the same name as the service; that conflicts with the one the CLI creates.

If a component already exists under the store's name, the CLI's create can succeed without creating a store. Provisioning detects that and fails, naming the component, instead of carrying on.

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

Components declared this way reconcile on every run (see [How provisioning behaves on re-run](#how-provisioning-behaves-on-re-run)).

### Subscriptions

`WithSubscription(name, component, topic, route)` declares a pub/sub subscription. The longer overload sets the bulk options; pass `null` to any of them to use the Catalyst default:

```csharp
.WithSubscription(
    "orders-created",
    "your-desired-pubsub-name",
    "orders",
    "/orders",
    bulk: true,
    bulkMaxMessagesCount: 100,
    bulkMaxAwaitDuration: null)
```

Generated subscription names are `{app}-pubsub-{topic}`.

**Duplicates are rejected.** Declaring the same app, component and topic under two subscription names throws, because it would deliver the topic twice. Reusing one subscription name for a different app, component or topic is rejected too.

### Agents and the default service names

Adding an agent with `WithCatalystAgent()` has a side effect worth knowing about: creating a project's **first** agent makes Catalyst provision a managed KV store named **`kvstore`** and a managed pub/sub named **`pubsub`** to back the agent registry — the same two names `WithCatalystKvStore()` and `WithCatalystPubSub()` default to. Two consequences:

- **Those services are created without a fronting component.** A later `kv create kvstore` is refused as "already exists", which `--ignore-if-exists` reports as success, so the component your app addresses would never appear. This integration detects that and creates the missing component itself, so `SaveStateAsync("kvstore", …)` works either way. You do not need to do anything.
- **They count against your plan's per-project limits.** If your organization allows one KV store per project, the agent's `kvstore` is it, and asking for a second under a different name fails with a quota error naming the region. Either reuse the default name or raise the limit with Diagrid support.

### Actor state stores

`WithCatalystKvStore()` does not set `actorStateStore`. With `EnableManagedWorkflow = true`, Catalyst already provisions a `workflows-state` store carrying `actorStateStore: "true"` (set in the Catalyst control plane's project controller), and the sidecar picks the first in-scope flagged store by map iteration order. A second flagged store would make that choice arbitrary.

If you want your own store to hold actor state, opt in explicitly through `DiagridStateStore`:

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

## How provisioning behaves on re-run

Every run compares what the AppHost declares against what is in Catalyst. What happens next depends on the resource:

| Resource | On re-run |
| --- | --- |
| Components | Reconciled (updated to match the AppHost) |
| Subscriptions | Reconciled |
| Project | Create-only |
| App | Create-only |
| Agent | Create-only |
| KV store | Create-only |
| Pub/sub | Create-only |

**Reconciled** means the AppHost is the source of truth: declared values are resent wholesale, so scopes or metadata you set in the portal or with the CLI are **overwritten** on the next run. Put them in the AppHost.

**Create-only** means a change you make in the AppHost is **ignored** if the resource already exists, and the run still succeeds. To change a region, a body size or another project setting, delete the resource and let the AppHost create it again.

`EnableWorkflowHistorySigning` is the one to watch. It can only be chosen when the project is created and cannot be changed afterwards, so setting it against an existing project silently does nothing.

## Upgrading from 1.x to 2.0

If you are upgrading an existing AppHost, read this section before you run it.

### Before you upgrade

These three change behavior **without any compile error**. The first affects live message delivery.

1. **Duplicate topic delivery until you delete the old subscription.** Generated subscription names changed from `{app}-pubsub` to `{app}-pubsub-{topic}`. The old scheme crashed as soon as an app subscribed to a second topic, so this was necessary. The consequence for an existing project: the first run creates the new subscription and leaves the old `{app}-pubsub` one in place, so **the topic is delivered twice** to your app. This looks like a bug in your app, not an incomplete upgrade. Delete the old subscription once per project and app:

   ```bash
   # for an app named "checkout":
   diagrid subscription delete checkout-pubsub --project your-project
   ```

   You can also delete it in the Catalyst portal. Subscriptions you named yourself through `WithSubscription(name, ...)` are not affected.

2. **Scopes and metadata are reset on every run.** Components and subscriptions now reconcile — see [How provisioning behaves on re-run](#how-provisioning-behaves-on-re-run). Scopes or metadata that you set in the portal or with the CLI are **overwritten**. Put them in the AppHost.

3. **`AddCatalystProject()` with no name now throws** for AppHosts that use top-level statements, which is the default Aspire template. This used to fall back silently to a project called `aspire`, which meant every such AppHost shared one project name in Catalyst. Pass a name: `AddCatalystProject("my-project")`. The exception names the `customProjectName` parameter.

### Breaking API changes

**Every interface and async method was renamed to follow the standard .NET conventions.** The package did not follow them before, and fixing it later would have cost another breaking release:

- `CatalystProvisioner` is now **`ICatalystProvisioner`**, and `CatalystComponent` / `CatalystComponent<T>` are now **`ICatalystComponent`** / **`ICatalystComponent<T>`**. Interfaces take the `I` prefix.
- **Every `Task`-returning method gained the `Async` suffix**: `CreateProject` → `CreateProjectAsync`, `GetAppDetails` → `GetAppDetailsAsync`, and so on for all 15 members of the interface. `Init` is now **`InitializeAsync`**.
- The generic parameters on the `WithCatalyst*` extension methods are now `TResource` and `TMetadata`. Type parameter names are not part of the call site, so this one does not affect your code.
- Errors raised by the `WithCatalyst*` builder methods are now `InvalidOperationException` rather than bare `System.Exception`, so you can catch them selectively. A `catch (Exception)` still works.

A find-and-replace of `CatalystProvisioner` → `ICatalystProvisioner` plus the `Async` suffix covers the whole migration.

### If you implement or subclass `ICatalystProvisioner`

`ICatalystProvisioner` is the swappable abstraction behind the CLI calls, and it also changed in several places:

- `CreateAppAsync` and `GetAppDetailsAsync` gained a `projectName` parameter.
- `GetProjectDetails` is replaced by `TryGetProjectDetailsAsync` and `WaitForProjectReadyAsync`.
- `InitializeAsync` gained an `ILogger` parameter.
- `CheckKvStoreExists` is replaced by `KvStoreExistsAsync` and `PubSubExistsAsync`, whose parameters are now named `projectName` and `cancellationToken` (they were `project` and `ct`).
- `CreateComponent` is now `UpsertComponentAsync`, and it gained a third parameter, `bool isManagedServiceComponent`.
- `CreateSubscription` is now `UpsertSubscriptionAsync`.
- `CreateAgentAsync` and `GetAgentDetailsAsync` are new.
- `CreateAppOptions` is now `CreateIdentityOptions`. Properties were renamed (for example `AppEndpoint` to `Endpoint`) and `Project` is now required.
- `CliAppDetails` and five CLI output DTOs were removed.
- The `ResolveApiToken` signature changed.
- `IgnoreIfExists` now defaults to `true` on four options records.

### Also new in 2.0

- **`WithCatalystApp()` is the canonical name** for turning a resource into a Catalyst app. `WithCatalyst()` still works, is not marked `[Obsolete]`, and behaves identically.
- **`WithCatalystAgent()`**, the `ProjectDescriptor` options, bulk subscriptions, duplicate-subscription rejection, managed-store collision detection, resource name validation and the CLI version floor are all documented in their sections above.
- **`WithCatalystKvStore()` no longer sets `actorStateStore`** — see [Actor state stores](#actor-state-stores).
- **The dashboard link follows `DIAGRID_URL`.** It used to be hardcoded to `catalyst.r1.diagrid.io`.
- **The `diagrid dev run` proxy starts in the AppHost's directory** instead of `.`.
- **`SampleKvName` is unchanged**: `WithCatalystKvStore()` with no name still creates `kvstore`. The demo AppHost names its store explicitly even though it uses the default, so the line is visible rather than implied.

## How to Release

The package is published to [NuGet.org](https://www.nuget.org/packages/Diagrid.Aspire.Hosting.Catalyst) by `.github/workflows/release.yaml`. There is one way to publish: **publish a GitHub Release**. The release's tag name becomes the package version verbatim.

### Choosing the version

Semantic versioning, and the tag is the bare version with **no `v` prefix** — `2.0.0`, not `v2.0.0`. The workflow rejects a tag that is not a bare semver version rather than publishing something malformed.

| Change | Bump |
| --- | --- |
| Removing or renaming anything public, or changing a signature | Major |
| New methods, options or overloads; new behavior behind a default | Minor |
| Fixes that leave the public surface alone | Patch |

Renaming `CatalystProvisioner` to `ICatalystProvisioner` and adding the `Async` suffix across the interface are breaking, so the release after `1.0.0` is `2.0.0`.

### Releasing

1. Merge to `main` and wait for CI to pass on it.
2. Rehearse first. Run the **Release** workflow manually (`workflow_dispatch`): it builds, runs all three test suites, packs, and uploads the `.nupkg` as a build artifact **without pushing anything**. Download it and check the version, the dependencies and the readme that ships inside it.
3. Draft a GitHub Release. Tag it with the bare version, targeting the commit on `main` you want to ship. For a major release, write the notes from the [Upgrading](#upgrading-from-1x-to-20) section — breaking changes first, because that is what tells someone whether the upgrade is safe.
4. Publish the release. The workflow runs again on the tagged commit, re-runs the tests, packs with the tag as the version, and pushes to NuGet.org.
5. Confirm the new version is listed on NuGet.org. It takes a few minutes to appear and be indexed.

### Things worth knowing before you publish

- **A published version is permanent.** NuGet does not allow republishing a version — a bad one can only be *unlisted*, and anyone who already referenced it keeps resolving it. This is why step 2 exists and why the workflow validates the tag.
- **The push does not use `--skip-duplicate`.** If the version already exists the job fails, rather than going green and leaving you believing you shipped.
- **Nothing publishes except a published release.** `workflow_dispatch` is pack-and-upload only. It used to push as well, which is how the placeholder version `0.0.0-development` reached NuGet.org.
- **The release needs the `NUGET_API_KEY` secret** to be present and unexpired. NuGet.org API keys expire; a push failing with a 401 usually means the key needs regenerating.
- **The package is MIT licensed.** The `LICENSE` file at the repo root and `PackageLicenseExpression` in `Hosting.Catalyst/Hosting.Catalyst.csproj` must stay in agreement — GitHub reads the first, NuGet.org renders the second.

## Additional Resources

See an example of this integration being used in the [Diagrid Catalyst Workflows demo](https://github.com/diagrid-labs/catalyst-order-workflow-dotnet).

In the `AppHost` demo in this repository, the OSS side of the `USE_CATALYST` switch keeps workflow state and order state in one **in-memory** store. Workflows are **not durable across a sidecar restart there**. The crash-and-resume demonstration only works on the Catalyst side.
