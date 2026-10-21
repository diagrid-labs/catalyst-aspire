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
        var root = (JsonObject)(Normalise(JsonNode.Parse(rawJson), names, null)
            ?? throw new InvalidOperationException($"{file}: CLI printed no JSON"));
        AssertNoSecrets(root, names);
        var ordered = new JsonObject { ["_capturedFrom"] = origin };
        foreach (var (key, value) in root.ToList())
        {
            root.Remove(key);
            ordered[key] = value;
        }

        File.WriteAllText(Path.Combine(dir, file), ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    internal static JsonNode? Normalise(JsonNode? node, string[] names, string? parentKey, bool inSecret = false)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    obj[key] = Normalise(value, names, key, inSecret || SecretKey().IsMatch(key));
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = Normalise(array[i], names, parentKey, inSecret);
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
        if (inSecret && (key is null || !SecretKey().IsMatch(key))) return Redacted;

        if (key is not null && SecretKey().IsMatch(key))
        {
            // Case-insensitive on purpose: ApiToken, accessToken, api_key, clientSecret, Password...
            return key.Contains("api", StringComparison.OrdinalIgnoreCase) && key.Contains("token", StringComparison.OrdinalIgnoreCase)
                ? MockApiToken
                : key.Equals("token", StringComparison.OrdinalIgnoreCase) || key.Equals("appToken", StringComparison.OrdinalIgnoreCase)
                    ? MockAppToken
                    : Redacted;
        }

        if (s.Contains("diagrid://", StringComparison.Ordinal)) return MockApiToken; // a token anywhere in the string
        if (Timestamp().IsMatch(s)) return "2026-01-01T00:00:00Z";
        if (Uuid().IsMatch(s)) return MockUuid;
        foreach (var name in names) s = s.Replace(name, "demo", StringComparison.Ordinal);
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

                    if (value is not null) AssertNoSecrets(value, names, $"{path}.{key}");
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item) AssertNoSecrets(item, names, $"{path}[{i}]");
                }
                break;
            case JsonValue value when value.TryGetValue<string>(out var s):
                if (s.Contains("diagrid://", StringComparison.Ordinal) && s != MockApiToken) throw new InvalidOperationException($"{path}: diagrid:// value other than the mock");
                if (Email().IsMatch(s) && !s.EndsWith("@example.com", StringComparison.Ordinal)) throw new InvalidOperationException($"{path}: e-mail address");
                if (s != MockUuid && OpaqueId().IsMatch(s)) throw new InvalidOperationException($"{path}: long opaque string");
                if (names.Any(n => s.Contains(n, StringComparison.Ordinal))) throw new InvalidOperationException($"{path}: throwaway name not normalised");
                break;
        }
    }

    [GeneratedRegex(@"^\d{4}-\d\d-\d\dT[\d:.]+(Z|[+-]\d\d:\d\d)$")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")]
    private static partial Regex Uuid();

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
