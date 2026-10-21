using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Diagrid.Aspire.Hosting.Catalyst;

/// <summary>
///     A swappable abstraction for interacting with Catalyst.
/// </summary>
public interface CatalystProvisioner
{
    /// <summary>
    ///     In case any provisioner requires some setup, fine to noop.
    /// </summary>
    /// <param name="logger">Where non-fatal preflight findings are reported.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task Init(ILogger logger, CancellationToken cancellationToken);

    /// <summary>
    ///     Reconciles a subscription: <c>subscription get</c> (exit code only), then <c>subscription update</c> when it
    ///     exists or <c>subscription create</c> when it does not, because <c>subscription create</c> has no
    ///     <c>--ignore-if-exists</c>. The AppHost is the source of truth: an update sends the declared topic, route,
    ///     component, scopes and bulk mode wholesale, replacing any change made outside the AppHost.
    /// </summary>
    /// <param name="projectName"></param>
    /// <param name="descriptor"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task UpsertSubscription(string projectName, SubscriptionDescriptor descriptor, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates a project in Catalyst.
    /// </summary>
    /// <param name="projectName"></param>
    /// <param name="descriptor"></param>
    /// <param name="cancellationToken"></param>
    Task CreateProject(string projectName, ProjectDescriptor descriptor, CancellationToken cancellationToken);

    /// <summary>
    ///     Use an existing project in Catalyst.
    /// </summary>
    /// <param name="projectName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task UseProject(string projectName, CancellationToken cancellationToken);

    /// <summary>
    ///     Gets details about a Catalyst project, or null while it is still coming up (no endpoints yet).
    /// </summary>
    /// <param name="projectName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<ProjectDetails?> TryGetProjectDetails(string projectName, CancellationToken cancellationToken);

    /// <summary>
    ///     Polls until the project reports its endpoints. Throws <see cref="System.TimeoutException"/> naming the
    ///     project and the time waited if it does not become ready within <paramref name="timeout"/>.
    /// </summary>
    /// <param name="projectName"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<ProjectDetails> WaitForProjectReady(string projectName, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates an app in Catalyst.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="projectName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task CreateApp(string name, string projectName, CancellationToken cancellationToken);

    /// <summary>
    ///     Gets details about an app in Catalyst.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="projectName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<AppDetails> GetAppDetails(string name, string projectName, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates an agent in Catalyst (<c>agent create --ignore-if-exists</c>).
    /// </summary>
    Task CreateAgent(string name, string projectName, CancellationToken cancellationToken);

    /// <summary>
    ///     Gets details about an agent in Catalyst. Its token is read from the embedded app identity.
    /// </summary>
    Task<AppDetails> GetAgentDetails(string name, string projectName, CancellationToken cancellationToken);

    /// <summary>
    ///     Reconciles a component: <c>component update</c> when <paramref name="isManagedServiceComponent"/> (its name is a
    ///     declared KV store or pub/sub, so the CLI already created it), otherwise <c>component create --ignore-if-exists</c>.
    ///     The caller decides from declared names; there is no <c>component get</c> probe.
    /// </summary>
    Task UpsertComponent(ComponentDescriptor descriptor, string projectName, bool isManagedServiceComponent, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates a Catalyst pubsub service.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task CreatePubSub(string name, PubSubDescriptor options, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates a Catalyst KV store.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task CreateKvStore(string name, KvStoreDescriptor options, CancellationToken cancellationToken);

    /// <summary>
    ///     Checks to see if a KV store exists (<c>kv get</c>, exit code only).
    /// </summary>
    Task<bool> KvStoreExists(string name, string project, CancellationToken ct);

    /// <summary>
    ///     Checks to see if a pub/sub broker exists (<c>pubsub get</c>, exit code only).
    /// </summary>
    Task<bool> PubSubExists(string name, string project, CancellationToken ct);
}
