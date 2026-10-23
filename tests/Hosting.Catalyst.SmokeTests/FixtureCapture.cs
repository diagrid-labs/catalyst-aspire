using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Diagrid.Aspire.Hosting.Catalyst.Cli;

namespace Hosting.Catalyst.SmokeTests;

/// <summary>
/// Re-captures tests/fixtures/*.json from a real CLI. The nightly workflow runs it and opens a PR if the files
/// change, so the diff is the drift signal. Skipped unless CATALYST_CAPTURE_FIXTURES_DIR names the output folder.
/// Output is normalised so a run on another day or account does not by itself produce a diff: tokens become the
/// mock values the offline tests assert on (a real token must never reach a public repo), names become "demo",
/// timestamps and ids become constants. project-get-pending.json and agent-get-no-token.json describe transient
/// or failure states a healthy account cannot produce, so they stay hand-maintained.
/// </summary>
public partial class FixtureCapture
{
    private const string MockApiToken = "diagrid://mock-api-token";
    private const string MockAppToken = "mock-app-token";
    private const string Redacted = "redacted";
    private const string MockUuid = "00000000-0000-0000-0000-000000000001";

    /// <summary>Stands in for a Catalyst resource id. Eight digits, so re-running the masks is a no-op.</summary>
    private const string MockNumericId = "00000000";

    // Every timestamp collapses to this one value so a capture on another day does not read as drift. It is written
    // in the shape the CLI actually emits -- space-separated, no zone -- verified against 1.109.0 on a live account:
    // metadata.createdAt "2026-10-20 05:17:48", status.updatedAt "2026-10-21 09:07:01.000". NOT ISO 8601.
    internal const string NormalisedTimestamp = "2026-01-01 00:00:00";

    [SkippableFact(Timeout = 25 * 60 * 1000)]
    public async Task Captures_fixtures_from_the_real_cli()
    {
        var outDir = SmokeProject.CaptureDirectory;
        Skip.If(outDir is null, "Set CATALYST_CAPTURE_FIXTURES_DIR to capture fixtures.");
        Directory.CreateDirectory(outDir!);

        var project = SmokeProject.NewName();
        const string app = "capture-app";
        const string agent = "capture-agent";
        try
        {
            var version = (await Run(["version"])).Replace("Version:", "").Trim().Split('\n')[0].Trim();
            var origin = $"diagrid CLI {version}, captured by the nightly workflow";

            await Run(["project", "create", project, "--wait"]);
            await Run(["app", "create", app, "--project", project, "--wait"]);
            await Run(["agent", "create", agent, "--project", project, "--wait"]);

            string[] names = [project, app, agent];
            Save(outDir!, "project-get-ready.json", await Run(["project", "get", project, "--output", "json"]), origin, names);
            Save(outDir!, "app-get.json", await Run(["app", "get", app, "--project", project, "--output", "json"]), origin, names);
            Save(outDir!, "agent-get.json", await Run(["agent", "get", agent, "--project", project, "--output", "json"]), origin, names);
            Save(outDir!, "appid-get.json", await Run(["appid", "get", app, "--project", project, "--output", "json"]), origin, names);
        }
        finally
        {
            await SmokeProject.DeleteAsync(project);
        }
    }

    private static async Task<string> Run(string[] args)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var result = await ProcessRunner.RunAsync(args, TimeSpan.FromMinutes(15), cts.Token);
        result.EnsureSuccess(args);
        return result.StdOut;
    }

    private static void Save(string dir, string file, string rawJson, string origin, string[] names)
    {
        var all = NamesFor(rawJson, names);
        var root = (JsonObject)(Normalise(JsonNode.Parse(rawJson), all, null)
            ?? throw new InvalidOperationException($"{file}: CLI printed no JSON"));
        AssertNoSecrets(root, all);
        var ordered = new JsonObject { ["_capturedFrom"] = origin };
        foreach (var (key, value) in root.ToList())
        {
            root.Remove(key);
            ordered[key] = value;
        }

        File.WriteAllText(Path.Combine(dir, file), ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>
    ///     The throwaway resource names, plus every cluster id the document mentions.
    /// </summary>
    /// <remarks>
    ///     A cluster id (<c>vxjiifii</c>) is a bare word with no pattern to match on, and it appears both under
    ///     its own key and inside every <c>spiffeId</c>, so masking the key would leave it in the URIs. Collecting
    ///     it up front and treating it like a resource name replaces it wherever it occurs. It becomes "demo"
    ///     along with everything else nameable in these fixtures, which keeps one placeholder rather than two.
    /// </remarks>
    internal static string[] NamesFor(string rawJson, string[] names)
    {
        var clusters = new SortedSet<string>(StringComparer.Ordinal);
        Collect(JsonNode.Parse(rawJson), null);
        return [.. names, .. clusters];

        void Collect(JsonNode? node, string? key)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (k, v) in obj)
                    {
                        Collect(v, k);
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        Collect(item, key);
                    }
                    break;
                case JsonValue value when value.TryGetValue<string>(out var s) && s.Length > 0:
                    if (key == "cluster")
                    {
                        clusters.Add(s);
                    }
                    else if (SpiffeCluster().Match(s) is { Success: true } spiffe)
                    {
                        // `agent get` carries spiffe://<cluster>.<region>... but no cluster key of its own, so
                        // collecting only from that key left the id in all three of its spiffeIds.
                        clusters.Add(spiffe.Groups[1].Value);
                    }

                    break;
            }
        }
    }

    internal static JsonNode? Normalise(JsonNode? node, string[] names, string? parentKey, bool inSecret = false)
    {
        switch (node)
        {
            // Containers are normalised IN PLACE and returned as the same reference, so only a genuine
            // replacement is written back. Assigning the same node into the slot it already occupies re-parents
            // a node that still has a parent: JsonObject's indexer detaches the old value first and gets away
            // with it, JsonArray.SetItem does not and throws "The node already has a parent".
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    var replacement = Normalise(value, names, key, inSecret || SecretKey().IsMatch(key));
                    if (!ReferenceEquals(replacement, value))
                    {
                        obj[key] = replacement;
                    }
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var item = array[i];
                    var replacement = Normalise(item, names, parentKey, inSecret);
                    if (!ReferenceEquals(replacement, item))
                    {
                        array[i] = replacement;
                    }
                }
                return array;
            case JsonValue value when value.TryGetValue<string>(out var s):
                return JsonValue.Create(NormaliseString(s, names, parentKey, inSecret));
            case JsonValue value when parentKey == "resourceVersion" && value.GetValueKind() == JsonValueKind.Number:
                return JsonValue.Create(1);
            default:
                return node;
        }
    }

    private static string NormaliseString(string s, string[] names, string? key, bool inSecret)
    {
        // Anything nested under a secret-named key (arrays and objects included) is secret, whatever its own key says.
        if (inSecret && (key is null || !SecretKey().IsMatch(key)))
        {
            return Redacted;
        }

        if (key is not null && SecretKey().IsMatch(key))
        {
            // Case-insensitive on purpose: ApiToken, accessToken, api_key, clientSecret, Password...
            return key.Contains("api", StringComparison.OrdinalIgnoreCase) && key.Contains("token", StringComparison.OrdinalIgnoreCase)
                ? MockApiToken
                : key.Equals("token", StringComparison.OrdinalIgnoreCase) || key.Equals("appToken", StringComparison.OrdinalIgnoreCase)
                    ? MockAppToken
                    : Redacted;
        }

        if (s.Contains("diagrid://", StringComparison.Ordinal))
        {
            return MockApiToken; // a token anywhere in the string
        }

        if (Timestamp().IsMatch(s))
        {
            return NormalisedTimestamp;
        }

        if (Uuid().IsMatch(s))
        {
            return MockUuid;
        }

        foreach (var name in names)
        {
            s = s.Replace(name, "demo", StringComparison.Ordinal);
        }

        // Catalyst resource ids are plain numbers, so Uuid() never matched them and they reached the fixtures
        // intact: a uid, and the project id inside every endpoint host and spiffeId. They change on every
        // capture, so leaving them in would also report drift every night for ever.
        s = NumericResourceId().Replace(s, MockNumericId);

        // A generated per-resource suffix (demo-agentcfg-283d6626), likewise different on every capture.
        // Runs after the id rule so an already-masked -00000000 is left as it is.
        s = GeneratedSuffix().Replace(s, "-" + MockNumericId);

        s = Email().Replace(s, "user@example.com");
        return OpaqueId().Replace(s, Redacted);
    }

    /// <summary>
    /// Fail-closed backstop run on the NORMALISED document, before it is written. The nightly pushes to a public
    /// repo, so anything credential-shaped that survived normalisation must stop the job, not be reviewed later.
    /// Throws naming the path only, never the value.
    /// </summary>
    internal static void AssertNoSecrets(JsonNode node, string[] names, string path = "$")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (value is JsonArray or JsonObject && SecretKey().IsMatch(key) && AnyNonMockString(value))
                    {
                        throw new InvalidOperationException($"{path}.{key}: secret-looking key holds a non-mock value");
                    }

                    if (value is JsonValue v && v.TryGetValue<string>(out var str) && SecretKey().IsMatch(key)
                        && str is not (MockApiToken or MockAppToken or Redacted))
                    {
                        throw new InvalidOperationException($"{path}.{key}: secret-looking key holds a non-mock value");
                    }

                    if (value is not null)
                    {
                        AssertNoSecrets(value, names, $"{path}.{key}");
                    }
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item)
                    {
                        AssertNoSecrets(item, names, $"{path}[{i}]");
                    }
                }
                break;
            case JsonValue value when value.TryGetValue<string>(out var s):
                if (s.Contains("diagrid://", StringComparison.Ordinal) && s != MockApiToken)
                {
                    throw new InvalidOperationException($"{path}: diagrid:// value other than the mock");
                }

                if (Email().IsMatch(s) && !s.EndsWith("@example.com", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}: e-mail address");
                }

                if (s != MockUuid && OpaqueId().IsMatch(s))
                {
                    throw new InvalidOperationException($"{path}: long opaque string");
                }

                if (names.Any(n => s.Contains(n, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException($"{path}: throwaway name not normalised");
                }

                break;
        }
    }

    // Both separators on purpose. The CLI emits "2026-10-20 05:17:48" (and ".000" on status.updatedAt); an
    // ISO-8601-only pattern leaves those untouched, so every nightly capture would differ from the last on
    // timestamps alone and open a drift PR that reports nothing. The zone suffix is optional for the same reason.
    [GeneratedRegex(@"^\d{4}-\d\d-\d\d[T ][\d:.]+(Z|[+-]\d\d:\d\d)?$")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")]
    private static partial Regex Uuid();

    // Six or more digits in a row: a Catalyst resource id, wherever it sits (a bare uid, or embedded in
    // "grpc-prj14272806.cloud.r1.diagrid.io"). Anchorless on purpose. Ports and resourceVersion are JSON
    // numbers, not strings, so they never reach this; a normalised timestamp has no run this long.
    [GeneratedRegex(@"\d{6,}")]
    private static partial Regex NumericResourceId();

    // A hyphen followed by exactly eight hex characters at the end of a word: the generated suffix on names
    // like demo-agentcfg-283d6626. Narrow on purpose -- "diagrid-aws-eu-west" has no eight-hex run.
    [GeneratedRegex(@"-[0-9a-f]{8}\b")]
    private static partial Regex GeneratedSuffix();

    // The cluster is the first label of a spiffe URI: spiffe://<cluster>.<region>...
    [GeneratedRegex(@"^spiffe://([^./]+)\.")]
    private static partial Regex SpiffeCluster();

    private static bool AnyNonMockString(JsonNode node) => node switch
    {
        JsonObject o => o.Any(kv => kv.Value is not null && AnyNonMockString(kv.Value)),
        JsonArray a => a.Any(i => i is not null && AnyNonMockString(i)),
        JsonValue v when v.TryGetValue<string>(out var str) => str is not (MockApiToken or MockAppToken or Redacted),
        _ => false,
    };

    [GeneratedRegex("token|key|secret|password|credential|bearer", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // 32+ chars of base64url/hex: a token, hash or opaque id. Spans a whole word so URLs are untouched.
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{32,}(?![A-Za-z0-9_-])")]
    private static partial Regex OpaqueId();
}
