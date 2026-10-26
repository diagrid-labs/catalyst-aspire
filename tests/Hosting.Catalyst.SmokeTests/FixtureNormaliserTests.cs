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

    // Normalise mutates objects and arrays in place and returns the SAME node, so assigning that node back into
    // the slot it already occupies re-parents a node that still has a parent. JsonObject's indexer detaches the
    // old value first and survives; JsonArray.SetItem does not, and throws "The node already has a parent".
    // Nothing caught it because every array in these tests held strings, which are replaced by NEW nodes.
    // `app get` returns status.appIds[] -- an array of objects -- so the first capture to reach a real CLI died.
    [Theory]
    [InlineData("""{"items":[{"a":"b"}]}""")]
    [InlineData("""{"items":[["a"]]}""")]
    [InlineData("""{"status":{"appIds":[{"status":{"apiToken":"diagrid://real-secret"}}]}}""")]
    [InlineData("""{"items":[{"a":"b"},{"c":"d"}]}""")]
    public void Arrays_of_objects_and_arrays_normalise_without_throwing(string json)
    {
        var text = Clean(json).ToJsonString();

        Assert.DoesNotContain("real-secret", text, StringComparison.Ordinal);
    }

    // The nested value must actually be normalised, not merely survive the walk.
    [Fact]
    public void A_secret_inside_an_array_of_objects_is_redacted()
    {
        var node = FixtureCapture.Normalise(
            JsonNode.Parse("""{"status":{"appIds":[{"status":{"apiToken":"diagrid://real-secret"}}]}}"""), Names, null)!;

        Assert.Equal("diagrid://mock-api-token", (string?)node["status"]!["appIds"]![0]!["status"]!["apiToken"]);
    }

    // Catalyst resource ids are plain numbers, which the UUID pattern does not match, so the first real capture
    // carried live ids and the hostnames built from them into fixtures bound for a public repo. They also change
    // on every run, which on its own would make the nightly report drift every night forever.
    [Theory]
    [InlineData("""{"uid":"14272806"}""", "uid")]
    [InlineData("""{"host":"grpc-prj14272806.cloud.r1.diagrid.io"}""", "host")]
    [InlineData("""{"url":"https://http-prj14272806.cloud.r1.diagrid.io:443"}""", "url")]
    [InlineData("""{"managed_by":"cra.diagrid.io/v1beta1/Agent/demo/14272819"}""", "managed_by")]
    public void Numeric_resource_ids_do_not_survive_normalisation(string json, string key)
    {
        var text = ((string?)Clean(json)[key])!;

        Assert.DoesNotContain("14272806", text, StringComparison.Ordinal);
        Assert.DoesNotContain("14272819", text, StringComparison.Ordinal);
    }

    // demo-agentcfg-283d6626: the suffix is generated per resource, so it differs on every capture.
    [Fact]
    public void Generated_hex_suffixes_do_not_survive_normalisation()
    {
        var text = ((string?)Clean("""{"appConfig":"demo-agentcfg-283d6626"}""")["appConfig"])!;

        Assert.DoesNotContain("283d6626", text, StringComparison.Ordinal);
        Assert.StartsWith("demo-agentcfg-", text, StringComparison.Ordinal);
    }

    // The cluster id appears both under its own key and inside every spiffeId, so masking the key alone is not
    // enough -- it has to be replaced wherever it occurs, the way the throwaway resource names already are.
    [Fact]
    public void A_cluster_id_is_replaced_everywhere_it_appears()
    {
        const string json = """
        {"status":{"instances":[{"cluster":"vxjiifii"}],
         "spiffeId":"spiffe://vxjiifii.diagrid-aws-eu-west.global.public.diagrid.io/ns/prj-14272806/demo"}}
        """;

        var text = FixtureCapture.Normalise(JsonNode.Parse(json), FixtureCapture.NamesFor(json, Names), null)!.ToJsonString();

        Assert.DoesNotContain("vxjiifii", text, StringComparison.Ordinal);
        Assert.DoesNotContain("14272806", text, StringComparison.Ordinal);
    }

    // `agent get` carries spiffeIds but no cluster key at all, so collecting only from that key left the real
    // cluster id in all three of its spiffeIds. The cluster is also the first label of the URI.
    [Fact]
    public void A_cluster_id_is_masked_even_when_only_a_spiffe_id_mentions_it()
    {
        const string json = """
        {"status":{"spiffeIds":["spiffe://vxjiifii.diagrid-aws-eu-west.global.public.diagrid.io/ns/prj-14272806/demo"]}}
        """;

        var text = FixtureCapture.Normalise(JsonNode.Parse(json), FixtureCapture.NamesFor(json, Names), null)!.ToJsonString();

        Assert.DoesNotContain("vxjiifii", text, StringComparison.Ordinal);
    }

    // The whole point is a capture that is identical from one night to the next.
    [Fact]
    public void Two_captures_of_the_same_shape_with_different_ids_normalise_identically()
    {
        const string monday = """
        {"metadata":{"uid":"14272806"},"status":{"instances":[{"cluster":"vxjiifii"}],
         "spiffeId":"spiffe://vxjiifii.x/ns/prj-14272806/demo",
         "endpoints":{"http":{"host":"http-prj14272806.cloud.r1.diagrid.io"}}},
         "spec":{"appConfig":"demo-agentcfg-283d6626"}}
        """;
        const string friday = """
        {"metadata":{"uid":"98311742"},"status":{"instances":[{"cluster":"qqzzbbnn"}],
         "spiffeId":"spiffe://qqzzbbnn.x/ns/prj-98311742/demo",
         "endpoints":{"http":{"host":"http-prj98311742.cloud.r1.diagrid.io"}}},
         "spec":{"appConfig":"demo-agentcfg-9f10ab77"}}
        """;

        static string Run(string json)
            => FixtureCapture.Normalise(JsonNode.Parse(json), FixtureCapture.NamesFor(json, Names), null)!.ToJsonString();

        Assert.Equal(Run(monday), Run(friday));
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
