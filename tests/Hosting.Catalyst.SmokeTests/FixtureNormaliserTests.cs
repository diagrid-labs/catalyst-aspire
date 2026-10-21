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
    [InlineData("""{"note":"bearer diagrid://real-secret trailing"}""")]
    [InlineData("""{"createdBy":"someone@corp.example.org"}""")]
    [InlineData("""{"orgId":"abcdefghijklmnopqrstuvwxyz0123456789"}""")]
    [InlineData("""{"u":"https://http.r1.diagrid.io/v1/aspire-smoke-0123456789ab"}""")]
    public void Secrets_never_survive_normalisation(string json)
    {
        var text = Clean(json).ToJsonString();

        Assert.DoesNotContain("real-secret", text);
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
    public void The_backstop_throws_on_anything_the_normaliser_did_not_clean(string json)
        => Assert.Throws<InvalidOperationException>(() => FixtureCapture.AssertNoSecrets(JsonNode.Parse(json)!, Names));

    [Fact]
    public void Mock_values_pass_the_backstop()
        => Clean("""{"apiToken":"diagrid://mock-api-token","token":"mock-app-token","u":"https://http.r1.diagrid.io/v1/demo"}""");
}
