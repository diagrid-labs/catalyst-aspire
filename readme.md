# Diagrid Catalyst Aspire Integration

![NuGet Version](https://img.shields.io/nuget/v/Diagrid.Aspire.Hosting.Catalyst)

This integration allows you to seamlessly connect your locally running Aspire projects to live Diagrid Catalyst infrastructure.

## What changed in this release

If you are upgrading an existing AppHost, read both items below before you run it. They change behavior without any compile error, and the first affects live message delivery. Separately, if your AppHost uses top-level statements (the default template) and calls `AddCatalystProject()` without a name, it now fails at startup; see [Startup failure without a project name](#startup-failure-without-a-project-name).

### Upgrade hazards

1. **Duplicate topic delivery until you delete the old subscription.** Generated subscription names changed from `{app}-pubsub` to `{app}-pubsub-{topic}`. The old scheme crashed as soon as an app subscribed to a second topic, so this was necessary. The consequence for an existing project: the first run creates the new subscription and leaves the old `{app}-pubsub` one in place, so **the topic is delivered twice** to your app. This looks like a bug in your app, not an incomplete upgrade. Delete the old subscription once per project and app:

   ```bash
   # for an app named "checkout":
   diagrid subscription delete checkout-pubsub --project your-project
   ```

   You can also delete it in the Catalyst portal. Subscriptions you named yourself through `WithSubscription(name, ...)` are not affected.

2. **Scopes and metadata are reset on every run.** Components and subscriptions now reconcile: each run compares the AppHost's declaration with Catalyst and updates the resource when they differ, resending the declared values wholesale. The AppHost is the source of truth. Scopes or metadata that you set in the portal or with the CLI are **overwritten** on the next run. Put them in the AppHost.

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
- **New `ProjectDescriptor` options:** `Region`, `RegionGroup`, `DeployManagedKv`, `DeployManagedPubsub`, `GlobalIdMaxBodySize`, `EnableWorkflowHistorySigning` and `DisableAppTunnels`. Previously there was no way to choose a region at all. All of them, including `DisableAppTunnels`, apply only when the project is created: this integration never runs `diagrid project update`, so on an existing project they are silently ignored (see above). `Region` and `RegionGroup` are mutually exclusive.

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
- **The Diagrid CLI is checked up front.** Provisioning checks the installed `diagrid` version and fails if it is older than 1.85.0, the oldest version this integration claims support for. If the version cannot be read, the check is skipped with a warning instead.
- **Resource names are validated.** A resource name that is not a valid Catalyst name is rejected, as is one whose derived `{name}-catalyst-proxy` resource would exceed Aspire's 64-character limit (so names over 49 characters). Resources with no HTTP endpoint, such as workflow-only workers, are now supported.
- **Duplicate subscriptions are rejected.** Declaring the same app, component and topic under two subscription names now throws, because it would deliver the topic twice. Reusing one subscription name for a different app, component or topic is rejected too.
- **A component that collides with a managed store is detected.** If a component already exists under the store's name, the CLI's create can succeed without creating a store. Provisioning now notices and fails, naming the component, instead of carrying on.
- **`WithCatalystKvStore()` and `WithCatalystPubSub()` do not add a component.** The CLI creates the fronting component, named after the store. Older XML docs said otherwise.

### `actorStateStore`

The convenience `WithCatalystKvStore()` no longer sets `actorStateStore`. With `EnableManagedWorkflow = true`, Catalyst already provisions a `workflows-state` store carrying `actorStateStore: "true"` (set in the Catalyst control plane's project controller), and the sidecar picks the first in-scope flagged store by map iteration order. A second flagged store would make that choice arbitrary. If you want your own store to hold actor state, opt in explicitly through `DiagridStateStore`:

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

### Startup failure without a project name

`AddCatalystProject()` with no name now throws for AppHosts that use top-level statements, which is the default Aspire template. Their entry point is `Program` in the global namespace, so there is no namespace to derive a name from. This used to fall back silently to a project called `aspire`, which meant every such AppHost shared one project name in Catalyst. Pass a name: `AddCatalystProject("my-project")`. The exception names the `customProjectName` parameter.

### Breaking API changes

**Every interface and async method was renamed to follow the standard .NET conventions.** The package did not follow them before, and fixing it later would have cost another breaking release:

- `CatalystProvisioner` is now **`ICatalystProvisioner`**, and `CatalystComponent` / `CatalystComponent<T>` are now **`ICatalystComponent`** / **`ICatalystComponent<T>`**. Interfaces take the `I` prefix.
- **Every `Task`-returning method gained the `Async` suffix**: `CreateProject` → `CreateProjectAsync`, `GetAppDetails` → `GetAppDetailsAsync`, and so on for all 15 members of the interface. `Init` is now **`InitializeAsync`**.
- The generic parameters on the `WithCatalyst*` extension methods are now `TResource` and `TMetadata`. Type parameter names are not part of the call site, so this one does not affect your code.

A find-and-replace of `CatalystProvisioner` → `ICatalystProvisioner` plus the `Async` suffix covers the whole migration.

`ICatalystProvisioner` is the swappable abstraction behind the CLI calls, and it also changed in several places. If you implement or subclass it, you are affected:

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
- `SampleKvName` is unchanged: `WithCatalystKvStore()` with no name still creates `kvstore`. Expect to pass a name instead — the store has to match the Dapr state store component your services already address, so moving off Dapr OSS usually means `WithCatalystKvStore("statestore")` or whatever yours is called. The demo AppHost does this, and it is the one line the switch costs.
- Errors raised by the `WithCatalyst*` builder methods are now `InvalidOperationException` rather than bare `System.Exception`, so you can catch them selectively. A `catch (Exception)` still works.

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
3. Draft a GitHub Release. Tag it `2.0.0`, targeting the commit on `main` you want to ship. Write the notes from the "What changed in this release" section above — breaking changes first, because that section is what tells someone whether the upgrade is safe.
4. Publish the release. The workflow runs again on the tagged commit, re-runs the tests, packs with the tag as the version, and pushes to NuGet.org.
5. Confirm the new version is listed on NuGet.org. It takes a few minutes to appear and be indexed.

### Things worth knowing before you publish

- **A published version is permanent.** NuGet does not allow republishing a version — a bad one can only be *unlisted*, and anyone who already referenced it keeps resolving it. This is why step 2 exists and why the workflow validates the tag.
- **The push does not use `--skip-duplicate`.** If the version already exists the job fails, rather than going green and leaving you believing you shipped.
- **Nothing publishes except a published release.** `workflow_dispatch` is pack-and-upload only. It used to push as well, which is how the placeholder version `0.0.0-development` reached NuGet.org.
- **The release needs the `NUGET_API_KEY` secret** to be present and unexpired. NuGet.org API keys expire; a push failing with a 401 usually means the key needs regenerating.
- **The package declares no licence.** NuGet.org shows "License not specified", and some organisations refuse such packages. Add a `LICENSE` file and the matching `PackageLicenseExpression` in `Hosting.Catalyst/Hosting.Catalyst.csproj` — there is a note in that file with the exact lines.

## Additional Resources

See an example of this integration being used in the [Diagrid Catalyst Workflows demo](https://github.com/diagrid-labs/catalyst-order-workflow-dotnet).
