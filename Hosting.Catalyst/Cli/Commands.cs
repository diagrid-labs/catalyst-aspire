using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Options;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Output;
using Diagrid.Aspire.Hosting.Catalyst.Model;
using Microsoft.Extensions.Logging;

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
    // --no-color=true: ProcessRunner appends it to every invocation.

    internal static string[] BuildCreateSubscriptionArgs(string name, CreateSubscriptionOptions options)
    {
        // subscription create is the one create with no --ignore-if-exists.
        var arguments = new List<string> { "subscription", "create", name };

        AddOptionalArgument(arguments, "--component", options.Component);
        AddOptionalArgument(arguments, "--topic", options.Topic);
        AddOptionalArgument(arguments, "--route", options.Route);
        AddScopesArgument(arguments, options.Scopes);
        AddFlagArgument(arguments, "--bulk", options.Bulk);
        AddBulkTuningArguments(arguments, options);
        AddOptionalArgument(arguments, "--project", options.Project);
        AddFlagArgument(arguments, "--wait", options.Wait);

        return arguments.ToArray();
    }

    /// <summary>
    ///     <c>subscription update</c> sends the declared values wholesale (the AppHost is the source of truth): topic,
    ///     route, component and scopes always, and <c>--bulk</c> explicitly true or false so a subscription can leave bulk
    ///     mode. An empty scope list is sent as <c>[]</c>: that convention is verified for <c>component update --scopes</c>
    ///     (documented as "Use [] for clearing up scopes") but only ASSUMED for <c>subscription update --scopes</c>, which
    ///     documents no such convention. Unreachable from the public API today (<c>WithSubscription</c> always scopes to
    ///     the app), but reachable by a third party calling <c>UpsertSubscriptionAsync</c>. The bulk count and duration are sent only when declared: the
    ///     CLI offers no way to unset them.
    /// </summary>
    internal static string[] BuildUpdateSubscriptionArgs(string name, CreateSubscriptionOptions options)
    {
        var arguments = new List<string> { "subscription", "update", name };

        AddOptionalArgument(arguments, "--component", options.Component);
        AddOptionalArgument(arguments, "--topic", options.Topic);
        AddOptionalArgument(arguments, "--route", options.Route);
        arguments.Add("--scopes");
        arguments.Add(options.Scopes.Count > 0 ? string.Join(",", options.Scopes) : "[]");
        arguments.Add(options.Bulk ? "--bulk=true" : "--bulk=false");
        AddBulkTuningArguments(arguments, options);
        AddOptionalArgument(arguments, "--project", options.Project);
        AddFlagArgument(arguments, "--wait", options.Wait);

        return arguments.ToArray();
    }

    public static async Task UpdateSubscriptionAsync(
        string name,
        CreateSubscriptionOptions options,
        CancellationToken cancellationToken
    )
    {
        var arguments = BuildUpdateSubscriptionArgs(name, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    internal static string[] BuildGetSubscriptionArgs(string name, string projectName)
        => ["subscription", "get", name, "--project", projectName];

    /// <summary>
    ///     Whether the subscription exists, by the exit code of <c>subscription get</c> alone. The CLI exits 1 for every
    ///     error, so a network or auth failure reads as "absent" here; the create that follows then fails loudly.
    /// </summary>
    public static async Task<bool> SubscriptionExistsAsync(
        string name,
        string projectName,
        CancellationToken cancellationToken = default
    )
    {
        var result = await ProcessRunner.RunAsync(BuildGetSubscriptionArgs(name, projectName), ReadTimeout, cancellationToken);
        return result.ExitCode == 0;
    }

    private static void AddBulkTuningArguments(List<string> arguments, CreateSubscriptionOptions options)
    {
        AddOptionalIntArgument(arguments, "--bulk-max-messages-count", options.BulkMaxMessagesCount);

        if (options.BulkMaxAwaitDuration is { } duration)
        {
            arguments.Add("--bulk-max-await-duration");
            arguments.Add(FormatGoDuration(duration));
        }
    }

    /// <summary>
    /// Formats as a Go duration (<c>2s</c>, <c>1m30s</c>, <c>1h</c>, <c>0.25s</c>): the CLI rejects <c>00:00:02</c>.
    /// Sub-second values are written as a fraction of a second, never with a <c>ms</c> unit.
    /// </summary>
    internal static string FormatGoDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "A bulk await duration cannot be negative.");
        }

        var totalMilliseconds = (long)Math.Round(duration.TotalMilliseconds);

        if (totalMilliseconds == 0)
        {
            return "0s";
        }

        var hours = totalMilliseconds / 3_600_000;
        var minutes = totalMilliseconds / 60_000 % 60;
        var seconds = totalMilliseconds / 1000 % 60;
        var milliseconds = totalMilliseconds % 1000;
        var text = new StringBuilder();

        if (hours > 0)
        {
            text.Append(hours).Append('h');
        }

        if (minutes > 0)
        {
            text.Append(minutes).Append('m');
        }

        if (seconds > 0 || milliseconds > 0)
        {
            text.Append(seconds);
            if (milliseconds > 0)
            {
                text.Append('.').Append(milliseconds.ToString("000", CultureInfo.InvariantCulture).TrimEnd('0'));
            }

            text.Append('s');
        }

        return text.ToString();
    }

    public static async Task CreateSubscriptionAsync(
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
    public static async Task UseCatalystAsync(CancellationToken cancellationToken)
    {
        await ProcessRunner.RunAsync(BuildUseCatalystArgs(), ReadTimeout, cancellationToken);
    }

    // 'diagrid version' has no --output flag (it is rejected as unknown); its plain output is parsed by key.
    internal static string[] BuildVersionArgs() => ["version"];

    /// <summary>
    /// Fails with an install message when the CLI is missing, and an upgrade message when it is below the floor.
    /// Fails OPEN (a warning) when the version cannot be read: the check only improves an error message.
    /// </summary>
    public static async Task EnsureSupportedVersionAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var arguments = BuildVersionArgs();
        CliResult result;

        try
        {
            result = await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);
        }
        catch (CatalystCliException ex) when (ex.InnerException is System.ComponentModel.Win32Exception missing)
        {
            throw CatalystCli.NotInstalled(arguments, missing);
        }

        if (result.ExitCode != 0)
        {
            logger.LogWarning("{Message}", CatalystCli.UnreadableVersionWarning($"'diagrid version' exited with code {result.ExitCode}"));
            return;
        }

        if (!CatalystCli.EnsureSupportedVersion(arguments, result.StdOut))
        {
            logger.LogWarning("{Message}", CatalystCli.UnreadableVersionWarning("'diagrid version' printed no recognisable 'Version:' line"));
        }
    }

    internal static string[] BuildWhoamiArgs() => ["whoami"];

    /// <summary>Fails with a 'diagrid login' message when the CLI has no signed-in user.</summary>
    public static async Task EnsureLoggedInAsync(CancellationToken cancellationToken)
    {
        var arguments = BuildWhoamiArgs();
        var result = await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw CatalystCli.NotLoggedIn(arguments, result.ExitCode, StdErrCleaner.Clean(result.StdErr));
        }
    }

    internal static string[] BuildCreateProjectArgs(string projectName, CreateProjectOptions? options = null)
    {
        var arguments = new List<string> { "project", "create", projectName };

        options ??= new();

        if (!string.IsNullOrWhiteSpace(options.Region) && !string.IsNullOrWhiteSpace(options.RegionGroup))
        {
            throw new ArgumentException(
                "--region and --region-group are mutually exclusive; set only one of Region or RegionGroup.",
                nameof(options));
        }

        AddOptionalArgument(arguments, "--region", options.Region);
        AddOptionalArgument(arguments, "--region-group", options.RegionGroup);
        AddOptionalArgument(arguments, "--global-id-max-body-size", options.GlobalIdMaxBodySize);
        if (options.EnableWorkflowHistorySigning is { } signing)
        {
            arguments.Add($"--enable-workflow-history-signing={(signing ? "true" : "false")}");
        }

        arguments.Add($"--deploy-managed-pubsub={(options.DeployManagedPubsub ? "true" : "false")}");
        arguments.Add($"--deploy-managed-kv={(options.DeployManagedKv ? "true" : "false")}");
        AddFlagArgument(arguments, "--enable-managed-workflow", options.EnableManagedWorkflow);
        AddFlagArgument(arguments, "--wait", options.Wait);
        AddFlagArgument(arguments, "--use", options.Use);
        AddFlagArgument(arguments, "--disable-app-tunnels", options.DisableAppTunnels);
        AddFlagArgument(arguments, "--ignore-if-exists", options.IgnoreIfExists);

        return arguments.ToArray();
    }

    public static async Task CreateProjectAsync(
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
    public static async Task UseProjectAsync(string projectName, CancellationToken cancellationToken)
    {
        await ProcessRunner.RunAsync(BuildUseProjectArgs(projectName), ReadTimeout, cancellationToken);
    }

    internal static string[] BuildGetProjectArgs(string projectName)
        => ["project", "get", projectName, "--output", "json"];

    public static async Task<CliProjectDetails> GetProjectDetailsAsync(string projectName,
        CancellationToken cancellationToken)
    {
        var arguments = BuildGetProjectArgs(projectName);
        var result = await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);

        result.EnsureSuccess(arguments);

        return ParseJson(result, arguments, CliProjectDetails.Parse);
    }

    private static string Noun(IdentityKind kind) => kind switch
    {
        IdentityKind.App => "app",
        IdentityKind.Agent => "agent",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity kind"),
    };

    internal static string[] BuildGetIdentityArgs(IdentityKind kind, string name, string project)
        => [Noun(kind), "get", name, "--project", project, "--output", "json"];

    public static async Task<CliIdentityDetails> GetIdentityDetailsAsync(
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

    public static async Task CreateIdentityAsync(
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

        AddMetadataArguments(arguments, descriptor);

        AddScopesArgument(arguments, descriptor.Scopes);
        AddOptionalArgument(arguments, "--project", options.Project);
        AddFlagArgument(arguments, "--wait", options.Wait);
        AddFlagArgument(arguments, "--ignore-if-exists", options.IgnoreIfExists);

        return arguments.ToArray();
    }

    public static async Task CreateComponentAsync(
        ComponentDescriptor descriptor,
        CreateComponentOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = BuildCreateComponentArgs(descriptor, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    /// <summary>
    ///     <c>component update</c> replaces metadata and scopes wholesale (no partial updates), so both are always sent in full.
    ///     It has no <c>--type</c> (a type cannot change) and no <c>--ignore-if-exists</c>. An empty scope list is sent as
    ///     <c>[]</c>, which is how the CLI clears scopes, so the declared "all apps" actually reconciles.
    /// </summary>
    internal static string[] BuildUpdateComponentArgs(ComponentDescriptor descriptor, CreateComponentOptions options)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Name))
        {
            throw new ArgumentException("Component name required", nameof(descriptor));
        }

        var arguments = new List<string> { "component", "update", descriptor.Name };

        AddMetadataArguments(arguments, descriptor);

        arguments.Add("--scopes");
        arguments.Add(descriptor.Scopes.Count > 0 ? string.Join(",", descriptor.Scopes) : "[]");
        AddOptionalArgument(arguments, "--project", options.Project);
        AddFlagArgument(arguments, "--wait", options.Wait);

        return arguments.ToArray();
    }

    public static async Task UpdateComponentAsync(
        ComponentDescriptor descriptor,
        CreateComponentOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = BuildUpdateComponentArgs(descriptor, options);
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

    public static async Task CreatePubSubAsync(
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

    public static async Task CreateKvStoreAsync(
        string kvStoreName,
        CreateKvStoreOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = BuildCreateKvStoreArgs(kvStoreName, options);
        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    internal static string[] BuildGetKvStoreArgs(string kvStoreName, string projectName)
        => ["kv", "get", kvStoreName, "--project", projectName];

    internal static string[] BuildGetPubSubArgs(string pubsubName, string projectName)
        => ["pubsub", "get", pubsubName, "--project", projectName];

    /// <summary>Whether the KV store exists, by the exit code of <c>kv get</c> alone (no output parsing).</summary>
    public static async Task<bool> KvStoreExistsAsync(
        string kvStoreName,
        string projectName,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(kvStoreName))
        {
            throw new ArgumentException("KV Store name required", nameof(kvStoreName));
        }

        var result = await ProcessRunner.RunAsync(BuildGetKvStoreArgs(kvStoreName, projectName), ReadTimeout, cancellationToken);
        return result.ExitCode == 0;
    }

    /// <summary>Whether the pub/sub broker exists, by the exit code of <c>pubsub get</c> alone (no output parsing).</summary>
    public static async Task<bool> PubSubExistsAsync(
        string pubsubName,
        string projectName,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(pubsubName))
        {
            throw new ArgumentException("Pub/Sub broker name required", nameof(pubsubName));
        }

        var result = await ProcessRunner.RunAsync(BuildGetPubSubArgs(pubsubName, projectName), ReadTimeout, cancellationToken);
        return result.ExitCode == 0;
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

    private static void AddOptionalArgument(List<string> arguments, string flag, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add(flag);
            arguments.Add(value);
        }
    }

    private static void AddFlagArgument(List<string> arguments, string flag, bool condition)
    {
        if (condition)
        {
            arguments.Add(flag);
        }
    }

    private static void AddRepeatedArgument(List<string> arguments, string flag, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            arguments.Add(flag);
            arguments.Add(value);
        }
    }

    private static void AddKeyValueArguments(List<string> arguments, string flag, IDictionary<string, string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var pair in values)
        {
            arguments.Add(flag);
            arguments.Add($"{pair.Key}={pair.Value}");
        }
    }

    private static void AddOptionalIntArgument(List<string> arguments, string flag, int? value)
    {
        if (value.HasValue)
        {
            arguments.Add(flag);
            arguments.Add(value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Each pair is ONE argv entry, so an embedded '=' (connection strings, base64) survives.</summary>
    private static void AddMetadataArguments(List<string> arguments, ComponentDescriptor descriptor)
    {
        var hasMetadata = false;

        foreach (var pair in descriptor.Metadata)
        {
            if (pair.Value is null)
            {
                continue;
            }

            hasMetadata = true;
            arguments.Add("--metadata");
            arguments.Add($"{pair.Key}={FormatMetadataValue(pair.Value)}");
        }

        if (!hasMetadata)
        {
            throw new ArgumentException("Component metadata required", nameof(descriptor));
        }
    }

    // Values arrive as CLR primitives from the fluent API but as JsonElement from a strongly-typed spec;
    // both must render booleans as lowercase true/false, which the CLI and Dapr expect.
    private static string FormatMetadataValue(object value) => value switch
    {
        bool b => b ? "true" : "false",
        JsonElement { ValueKind: JsonValueKind.True } => "true",
        JsonElement { ValueKind: JsonValueKind.False } => "false",
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? string.Empty,
        JsonElement e => e.GetRawText(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static void AddScopesArgument(List<string> arguments, IList<string> scopes)
    {
        if (scopes.Count > 0)
        {
            arguments.Add("--scopes");
            arguments.Add(string.Join(",", scopes));
        }
    }
}
