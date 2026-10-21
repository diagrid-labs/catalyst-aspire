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

    public static async Task CreateSubscription(
        string name,
        CreateSubscriptionOptions options,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string>
        {
            "subscription",
            "create",
            name,
        };

        AddOptionalArgument(arguments, "--component", options.Component);
        AddOptionalArgument(arguments, "--topic", options.Topic);
        AddOptionalArgument(arguments, "--route", options.Route);
        AddScopesArgument(arguments, options.Scopes);
        AddOptionalArgument(arguments, "--project", options.Project);
        AddFlagArgument(arguments, "--wait", options.Wait);

        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }
    
    public static async Task UseCatalyst(CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "product",
            "use",
            "catalyst",
        };

        await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);
    }

    public static async Task CreateProject(
        string projectName,
        CreateProjectOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = new List<string>
        {
            "project",
            "create",
            projectName,
        };

        options ??= new();

        AddOptionalArgument(arguments, "--region", options.Region);

        var deployManagedPubsub = options.DeployManagedPubsub ? "true" : "false";
        arguments.AddRange([$"--deploy-managed-pubsub={deployManagedPubsub}"]);

        var deployManagedKv = options.DeployManagedKv ? "true" : "false";
        arguments.AddRange([$"--deploy-managed-kv={deployManagedKv}"]);

        AddFlagArgument(arguments, "--enable-managed-workflow", options.EnableManagedWorkflow);
        AddFlagArgument(arguments, "--wait", options.Wait);
        AddFlagArgument(arguments, "--use", options.Use);
        AddFlagArgument(arguments, "--disable-app-tunnels", options.DisableAppTunnels);

        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    public static async Task UseProject(string projectName, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "project",
            "use",
            projectName,
        };

        await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);
    }

    public static async Task<CliProjectDetails> GetProjectDetails(string projectName,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "project",
            "get",
            projectName,
            "--output",
            "json",
        };

        var result = await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);

        result.EnsureSuccess(arguments);

        var projectDetails = ParseJson(result, arguments, json => JsonSerializer.Deserialize<CliProjectDetails>(json));

        return projectDetails ?? throw new InvalidOperationException("Failed to deserialize project details");
    }

    public static async Task<CliAppDetails> GetAppDetails(string appId, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "appid",
            "get",
            appId,
            "--output",
            "json",
        };

        var result = await ProcessRunner.RunAsync(arguments, ReadTimeout, cancellationToken);

        result.EnsureSuccess(arguments);

        var appIdentityDetails = ParseJson(result, arguments, json => JsonSerializer.Deserialize<CliAppDetails>(json));

        return appIdentityDetails ?? throw new InvalidOperationException("Failed to deserialize app identity details");
    }

    public static async Task CreateApp(
        string appId,
        CreateAppOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = new List<string>
        {
            "appid",
            "create",
            appId,
        };

        options ??= new();

        AddOptionalArgument(arguments, "--project", options.Project);
        AddOptionalArgument(arguments, "--app-endpoint", options.AppEndpoint);
        AddOptionalArgument(arguments, "--app-token", options.AppToken);
        AddOptionalArgument(arguments, "--app-protocol", options.AppProtocol);
        AddOptionalArgument(arguments, "--app-config", options.AppConfig);
        AddFlagArgument(arguments, "--enable-app-health-check", options.EnableAppHealthCheck);
        AddOptionalArgument(arguments, "--app-health-check-path", options.AppHealthCheckPath);
        AddOptionalIntArgument(arguments, "--app-health-probe-interval", options.AppHealthProbeInterval);
        AddOptionalIntArgument(arguments, "--app-health-probe-timeout", options.AppHealthProbeTimeout);
        AddOptionalIntArgument(arguments, "--app-health-threshold", options.AppHealthThreshold);
        AddOptionalIntArgument(arguments, "--app-channel-timeout-seconds", options.AppChannelTimeoutSeconds);
        AddFlagArgument(arguments, "--wait", options.Wait);

        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    public static async Task CreateComponent(
        ComponentDescriptor descriptor,
        CreateComponentOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = new List<string>
        {
            "component",
            "create",
        };

        if (string.IsNullOrWhiteSpace(descriptor.Name))
        {
            throw new ArgumentException("Component name required", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Type))
        {
            throw new ArgumentException("Component type required", nameof(descriptor));
        }

        arguments.Add(descriptor.Name);
        arguments.Add("--type");
        arguments.Add(descriptor.Type);

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

        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    public static async Task CreatePubSub(
        string pubsubName,
        CreatePubSubOptions options,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(pubsubName))
        {
            throw new ArgumentException("Pub/Sub broker name required", nameof(pubsubName));
        }

        var arguments = new List<string>
        {
            "pubsub",
            "create",
            pubsubName,
        };

        AddOptionalArgument(arguments, "--project", options.Project);
        AddScopesArgument(arguments, options.Scopes);
        AddFlagArgument(arguments, "--wait", options.Wait);

        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

    public static async Task CreateKvStore(
        string kvStoreName,
        CreateKvStoreOptions options,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(kvStoreName))
        {
            throw new ArgumentException("KV Store name required", nameof(kvStoreName));
        }

        var arguments = new List<string>
        {
            "kv",
            "create",
            kvStoreName,
        };

        AddOptionalArgument(arguments, "--project", options.Project);
        AddScopesArgument(arguments, options.Scopes);
        AddFlagArgument(arguments, "--wait", options.Wait);

        var result = await ProcessRunner.RunAsync(arguments, CreateTimeout, cancellationToken);

        result.EnsureSuccess(arguments);
    }

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

        var arguments = new List<string>
        {
            "kv",
            "list",
            "--output",
            "json",
        };

        arguments.Add("--project");
        arguments.Add(projectName);

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
    private static T ParseJson<T>(CliResult result, IReadOnlyList<string> arguments, Func<string, T> parse)
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
