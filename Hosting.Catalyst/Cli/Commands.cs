using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Options;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Output;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Diagrid.Aspire.Hosting.Catalyst.Cli;

internal static class Commands
{
    /// <summary>Read and short calls (get, list, use): a hang here is a dead dashboard, so fail fast.</summary>
    internal static readonly TimeSpan ReadTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Creates that provision remote infrastructure (usually with --wait). Killing one mid-provision leaves a
    /// half-created resource, so allow a slow region time to finish.
    /// </summary>
    internal static readonly TimeSpan CreateTimeout = TimeSpan.FromMinutes(15);

    // Argument builders are pure so unit tests can assert on them without a process. They never include
    // --no-color: ProcessRunner appends it to every invocation.

    internal static string[] BuildCreateSubscriptionArgs(string name, CreateSubscriptionOptions options)
    {
        // subscription create is the one create with no --ignore-if-exists.
        var arguments = new List<string> { "subscription", "create", name };

        AddOptionalArgument(arguments, "--component", options.Component);
        AddOptionalArgument(arguments, "--topic", options.Topic);
        AddOptionalArgument(arguments, "--route", options.Route);
        AddScopesArgument(arguments, options.Scopes);
        AddOptionalArgument(arguments, "--project", options.Project);
        AddFlagArgument(arguments, "--wait", options.Wait);

        return arguments.ToArray();
    }

    public static async Task CreateSubscription(
        string name,
        CreateSubscriptionOptions options,
        CancellationToken cancellationToken
    )
    {
        var arguments = BuildCreateSubscriptionArgs(name, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    internal static string[] BuildUseCatalystArgs() => ["product", "use", "catalyst"];

    /// <summary>Best-effort courtesy: every other call passes --project, so nothing depends on this.</summary>
    public static async Task UseCatalyst(CancellationToken cancellationToken)
    {
        await ProcessRunner.RunAsync(BuildUseCatalystArgs(), ReadTimeout, cancellationToken);
    }

    internal static string[] BuildCreateProjectArgs(string projectName, CreateProjectOptions? options = null)
    {
        var arguments = new List<string> { "project", "create", projectName };

        options ??= new();

        AddOptionalArgument(arguments, "--region", options.Region);
        arguments.Add($"--deploy-managed-pubsub={(options.DeployManagedPubsub ? "true" : "false")}");
        arguments.Add($"--deploy-managed-kv={(options.DeployManagedKv ? "true" : "false")}");
        AddFlagArgument(arguments, "--enable-managed-workflow", options.EnableManagedWorkflow);
        AddFlagArgument(arguments, "--wait", options.Wait);
        AddFlagArgument(arguments, "--use", options.Use);
        AddFlagArgument(arguments, "--disable-app-tunnels", options.DisableAppTunnels);
        AddFlagArgument(arguments, "--ignore-if-exists", options.IgnoreIfExists);

        return arguments.ToArray();
    }

    public static async Task CreateProject(
        string projectName,
        CreateProjectOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = BuildCreateProjectArgs(projectName, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    internal static string[] BuildUseProjectArgs(string projectName) => ["project", "use", projectName];

    /// <summary>Best-effort courtesy: every other call passes --project, so nothing depends on this.</summary>
    public static async Task UseProject(string projectName, CancellationToken cancellationToken)
    {
        await ProcessRunner.RunAsync(BuildUseProjectArgs(projectName), ReadTimeout, cancellationToken);
    }

    internal static string[] BuildGetProjectArgs(string projectName)
        => ["project", "get", projectName, "--output", "json"];

    public static async Task<CliProjectDetails> GetProjectDetails(string projectName,
        CancellationToken cancellationToken)
    {
        var arguments = BuildGetProjectArgs(projectName);
        var result = await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);

        result.EnsureSuccess(arguments);

        var projectDetails = ParseJson(result, arguments, json => JsonSerializer.Deserialize<CliProjectDetails>(json));

        return projectDetails ?? throw new InvalidOperationException("Failed to deserialize project details");
    }

    private static string Noun(IdentityKind kind) => kind switch
    {
        IdentityKind.App => "app",
        IdentityKind.Agent => "agent",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity kind"),
    };

    internal static string[] BuildGetIdentityArgs(IdentityKind kind, string name, string project)
        => [Noun(kind), "get", name, "--project", project, "--output", "json"];

    public static async Task<CliIdentityDetails> GetIdentityDetails(
        IdentityKind kind,
        string name,
        string project,
        CancellationToken cancellationToken
    )
    {
        var arguments = BuildGetIdentityArgs(kind, name, project);
        var result = await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);

        result.EnsureSuccess(arguments);

        var details = ParseJson(result, arguments, json => JsonSerializer.Deserialize<CliIdentityDetails>(json));

        return details ?? throw new InvalidOperationException($"Failed to deserialize {Noun(kind)} details");
    }

    internal static string[] BuildCreateIdentityArgs(IdentityKind kind, string name, CreateIdentityOptions options)
    {
        var arguments = new List<string> { Noun(kind), "create", name };

        AddOptionalArgument(arguments, "--project", options.Project);
        AddOptionalArgument(arguments, "--endpoint", options.Endpoint);
        AddOptionalArgument(arguments, "--endpoint-token", options.EndpointToken);
        AddOptionalArgument(arguments, "--endpoint-protocol", options.EndpointProtocol);

        // `agent create` does not accept these flags; passing one would fail the whole command.
        if (kind == IdentityKind.App)
        {
            AddOptionalArgument(arguments, "--max-body-size", options.MaxBodySize);
            AddOptionalArgument(arguments, "--app-config", options.AppConfig);
            AddFlagArgument(arguments, "--enable-app-health-check", options.EnableAppHealthCheck);
            AddOptionalArgument(arguments, "--app-health-check-path", options.AppHealthCheckPath);
            AddOptionalIntArgument(arguments, "--app-health-probe-interval", options.AppHealthProbeInterval);
            AddOptionalIntArgument(arguments, "--app-health-probe-timeout", options.AppHealthProbeTimeout);
            AddOptionalIntArgument(arguments, "--app-health-threshold", options.AppHealthThreshold);
            AddOptionalIntArgument(arguments, "--app-channel-timeout-seconds", options.AppChannelTimeoutSeconds);
        }

        AddRepeatedArgument(arguments, "--enable-workflow-archive", options.EnableWorkflowArchive);

        if (kind == IdentityKind.App)
        {
            AddRepeatedArgument(arguments, "--enable-workflow-notifications", options.EnableWorkflowNotifications);
            AddKeyValueArguments(arguments, "--label", options.Labels);
            AddKeyValueArguments(arguments, "--annotation", options.Annotations);
        }

        AddFlagArgument(arguments, "--wait", options.Wait);
        AddFlagArgument(arguments, "--ignore-if-exists", options.IgnoreIfExists);

        return arguments.ToArray();
    }

    public static async Task CreateIdentity(
        IdentityKind kind,
        string name,
        CreateIdentityOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = BuildCreateIdentityArgs(kind, name, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    internal static string[] BuildCreateComponentArgs(ComponentDescriptor descriptor, CreateComponentOptions options)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Name))
        {
            throw new ArgumentException("Component name required", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Type))
        {
            throw new ArgumentException("Component type required", nameof(descriptor));
        }

        var arguments = new List<string> { "component", "create", descriptor.Name, "--type", descriptor.Type };

        var hasMetadata = false;

        foreach (var pair in descriptor.Metadata)
        {
            if (pair.Value is null) continue;

            hasMetadata = true;
            arguments.Add("--metadata");
            arguments.Add($"{pair.Key}={pair.Value}");
        }

        if (! hasMetadata)
        {
            throw new ArgumentException("Component metadata required", nameof(descriptor));
        }

        AddScopesArgument(arguments, descriptor.Scopes);
        AddOptionalArgument(arguments, "--project", options.Project);
        AddFlagArgument(arguments, "--wait", options.Wait);
        AddFlagArgument(arguments, "--ignore-if-exists", options.IgnoreIfExists);

        return arguments.ToArray();
    }

    public static async Task CreateComponent(
        ComponentDescriptor descriptor,
        CreateComponentOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = BuildCreateComponentArgs(descriptor, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    internal static string[] BuildCreatePubSubArgs(string pubsubName, CreatePubSubOptions options)
    {
        if (string.IsNullOrWhiteSpace(pubsubName))
        {
            throw new ArgumentException("Pub/Sub broker name required", nameof(pubsubName));
        }

        var arguments = new List<string> { "pubsub", "create", pubsubName };

        AddOptionalArgument(arguments, "--project", options.Project);
        AddScopesArgument(arguments, options.Scopes);
        AddFlagArgument(arguments, "--wait", options.Wait);
        AddFlagArgument(arguments, "--ignore-if-exists", options.IgnoreIfExists);

        return arguments.ToArray();
    }

    public static async Task CreatePubSub(
        string pubsubName,
        CreatePubSubOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = BuildCreatePubSubArgs(pubsubName, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    internal static string[] BuildCreateKvStoreArgs(string kvStoreName, CreateKvStoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(kvStoreName))
        {
            throw new ArgumentException("KV Store name required", nameof(kvStoreName));
        }

        var arguments = new List<string> { "kv", "create", kvStoreName };

        AddOptionalArgument(arguments, "--project", options.Project);
        AddScopesArgument(arguments, options.Scopes);
        AddFlagArgument(arguments, "--wait", options.Wait);
        AddFlagArgument(arguments, "--ignore-if-exists", options.IgnoreIfExists);

        return arguments.ToArray();
    }

    public static async Task CreateKvStore(
        string kvStoreName,
        CreateKvStoreOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = BuildCreateKvStoreArgs(kvStoreName, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    internal static string[] BuildListKvStoresArgs(string projectName)
        => ["kv", "list", "--output", "json", "--project", projectName];

    public static async Task<bool> CheckKvStoreExists(
        string kvStoreName,
        string projectName,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(kvStoreName))
        {
            throw new ArgumentException("KV Store name required", nameof(kvStoreName));
        }

        var arguments = BuildListKvStoresArgs(projectName);
        var result = await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);

        result.EnsureSuccess(arguments);

        return ParseJson(result, arguments, json =>
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("items")
                .EnumerateArray()
                .Any((kv) => kv.GetProperty("metadata").GetProperty("name").GetString() == kvStoreName);
        });
    }

    /// <summary>
    /// Parses stdout, reporting a cut-off response as truncation rather than a bare JSON error. Truncation is
    /// reported only when the runner flagged it; an unflagged parse failure is not proof the output was whole.
    /// </summary>
    internal static T ParseJson<T>(CliResult result, IReadOnlyList<string> arguments, Func<string, T> parse)
    {
        try
        {
            return parse(result.StdOut);
        }
        catch (JsonException ex) when (result.OutputTruncated)
        {
            throw new CatalystCliException(
                arguments,
                result.ExitCode,
                StdErrCleaner.Clean(result.StdErr),
                $"'diagrid {string.Join(" ", arguments)}' output was cut off before it could be read as JSON "
                    + "(a background process kept the CLI's output pipes open).",
                ex);
        }
    }

    private static void AddOptionalArgument(IList<string> arguments, string flag, string? value)
    {
        if (! string.IsNullOrWhiteSpace(value))
        {
            arguments.Add(flag);
            arguments.Add(value);
        }
    }

    private static void AddFlagArgument(IList<string> arguments, string flag, bool condition)
    {
        if (condition)
        {
            arguments.Add(flag);
        }
    }

    private static void AddRepeatedArgument(IList<string> arguments, string flag, IEnumerable<string>? values)
    {
        if (values is null) return;

        foreach (var value in values)
        {
            arguments.Add(flag);
            arguments.Add(value);
        }
    }

    private static void AddKeyValueArguments(IList<string> arguments, string flag, IDictionary<string, string>? values)
    {
        if (values is null) return;

        foreach (var pair in values)
        {
            arguments.Add(flag);
            arguments.Add($"{pair.Key}={pair.Value}");
        }
    }

    private static void AddOptionalIntArgument(IList<string> arguments, string flag, int? value)
    {
        if (value.HasValue)
        {
            arguments.Add(flag);
            arguments.Add(value.Value.ToString());
        }
    }

    private static void AddScopesArgument(IList<string> arguments, IList<string> scopes)
    {
        if (scopes.Count > 0)
        {
            arguments.Add("--scopes");
            arguments.Add(string.Join(",", scopes));
        }
    }
}
