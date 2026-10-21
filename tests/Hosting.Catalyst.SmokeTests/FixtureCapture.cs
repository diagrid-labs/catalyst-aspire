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
        var ordered = new JsonObject { ["_capturedFrom"] = origin };
        foreach (var (key, value) in root.ToList())
        {
            root.Remove(key);
            ordered[key] = value;
        }

        File.WriteAllText(Path.Combine(dir, file), ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static JsonNode? Normalise(JsonNode? node, string[] names, string? parentKey)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    obj[key] = Normalise(value, names, key);
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = Normalise(array[i], names, parentKey);
                }
                return array;
            case JsonValue value when value.TryGetValue<string>(out var s):
                return JsonValue.Create(NormaliseString(s, names, parentKey));
            case JsonValue value when parentKey == "resourceVersion" && value.GetValueKind() == JsonValueKind.Number:
                return JsonValue.Create(1);
            default:
                return node;
        }
    }

    private static string NormaliseString(string s, string[] names, string? key)
    {
        if (s.StartsWith("diagrid://", StringComparison.Ordinal) || key == "apiToken") return MockApiToken;
        if (key is "token" or "appToken") return MockAppToken;
        if (Timestamp().IsMatch(s)) return "2026-01-01T00:00:00Z";
        if (Uuid().IsMatch(s)) return "00000000-0000-0000-0000-000000000001";
        foreach (var name in names) s = s.Replace(name, "demo", StringComparison.Ordinal);
        return s;
    }

    [GeneratedRegex(@"^\d{4}-\d\d-\d\dT[\d:.]+(Z|[+-]\d\d:\d\d)$")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")]
    private static partial Regex Uuid();
}
