using System.Text.Json.Nodes;

namespace Hosting.Catalyst.SmokeTests;

/// <summary>
/// Offline: these never touch an account and always run. They pin the capture's secret handling, because the
/// nightly pushes captured fixtures to a public repo.
/// </summary>
public class FixtureNormaliserTests
{
    private static readonly string[] Names = ["aspire-smoke-0123456789ab", "capture-app"];

    private static JsonNode Clean(string json)
    {
        var node = FixtureCapture.Normalise(JsonNode.Parse(json), Names, null)!;
        FixtureCapture.AssertNoSecrets(node, Names);
        return node;
    }

    [Theory]
    [InlineData("""{"apiToken":"diagrid://real-secret"}""")]
    [InlineData("""{"ApiToken":"diagrid://real-secret"}""")]
    [InlineData("""{"accessToken":"abc"}""")]
    [InlineData("""{"api_key":"abc"}""")]
    [InlineData("""{"clientSecret":"abc"}""")]
    [InlineData("""{"Password":"abc"}""")]
    [InlineData("""{"tokens":["hunter2"]}""")]
    [InlineData("""{"credentials":{"a":{"b":"hunter2"}}}""")]
    [InlineData("""{"note":"bearer diagrid://real-secret trailing"}""")]
    [InlineData("""{"createdBy":"someone@corp.example.org"}""")]
    [InlineData("""{"orgId":"abcdefghijklmnopqrstuvwxyz0123456789"}""")]
    [InlineData("""{"u":"https://http.r1.diagrid.io/v1/aspire-smoke-0123456789ab"}""")]
    public void Secrets_never_survive_normalisation(string json)
    {
        var text = Clean(json).ToJsonString();

        Assert.DoesNotContain("real-secret", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("someone", text);
        Assert.DoesNotContain("abcdefghij", text);
        Assert.DoesNotContain("aspire-smoke-0123456789ab", text);
        Assert.DoesNotContain("\"abc\"", text);
    }

    // The backstop must fail closed on its own, independent of the normaliser.
    [Theory]
    [InlineData("""{"apiKey":"abc"}""")]
    [InlineData("""{"a":"x diagrid://real y"}""")]
    [InlineData("""{"a":"me@corp.io"}""")]
    [InlineData("""{"a":"abcdefghijklmnopqrstuvwxyz0123456789"}""")]
    [InlineData("""{"a":"capture-app"}""")]
    [InlineData("""{"tokens":["abc"]}""")]
    [InlineData("""{"credentials":{"a":{"b":"hunter2"}}}""")]
    public void The_backstop_throws_on_anything_the_normaliser_did_not_clean(string json)
        => Assert.Throws<InvalidOperationException>(() => FixtureCapture.AssertNoSecrets(JsonNode.Parse(json)!, Names));

    [Fact]
    public void Mock_values_pass_the_backstop()
        => Clean("""{"apiToken":"diagrid://mock-api-token","token":"mock-app-token","u":"https://http.r1.diagrid.io/v1/demo"}""");

    // The shapes the real CLI emits come first: 1.109.0 writes metadata.createdAt as "2026-10-20 05:17:48" and
    // status.updatedAt as "2026-10-21 09:07:01.000". An ISO-8601-only normaliser left those verbatim, so each
    // nightly capture differed from the last on timestamps alone and the drift PR reported nothing real.
    [Theory]
    [InlineData("2026-10-20 05:17:48")]
    [InlineData("2026-10-21 09:07:01.000")]
    [InlineData("2026-10-21T09:00:00Z")]
    [InlineData("2026-10-21T09:00:00.1234Z")]
    [InlineData("2026-10-21T09:00:00+02:00")]
    public void Every_timestamp_the_cli_emits_collapses_to_one_constant(string timestamp)
    {
        var node = FixtureCapture.Normalise(JsonNode.Parse($$"""{"createdAt":{{System.Text.Json.JsonSerializer.Serialize(timestamp)}}}"""), Names, null)!;

        Assert.Equal(FixtureCapture.NormalisedTimestamp, (string?)node["createdAt"]);
    }

    // Two captures taken on different days must produce byte-identical documents, or the nightly opens a PR a
    // reviewer cannot act on. This is the property the drift signal rests on, asserted end to end.
    [Fact]
    public void Two_captures_on_different_days_normalise_identically()
    {
        const string monday = """{"metadata":{"createdAt":"2026-10-20 05:17:48","updatedAt":"2026-10-20 05:21:47"},"status":{"updatedAt":"2026-10-20 06:00:00.714"}}""";
        const string friday = """{"metadata":{"createdAt":"2026-10-24 22:03:11","updatedAt":"2026-10-24 22:59:02"},"status":{"updatedAt":"2026-10-24 23:14:05.002"}}""";

        Assert.Equal(Clean(monday).ToJsonString(), Clean(friday).ToJsonString());
    }

    // Guards the widened pattern: it must still only match timestamps.
    [Theory]
    [InlineData("not a date")]
    [InlineData("demo")]
    [InlineData("2026-10-20")]
    [InlineData("aws-eu-central-1")]
    public void Non_timestamps_are_left_alone(string value)
    {
        var node = FixtureCapture.Normalise(JsonNode.Parse($$"""{"field":{{System.Text.Json.JsonSerializer.Serialize(value)}}}"""), Names, null)!;

        Assert.Equal(value, (string?)node["field"]);
    }
}
