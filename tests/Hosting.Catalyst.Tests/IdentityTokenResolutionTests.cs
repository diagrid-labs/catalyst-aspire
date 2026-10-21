using System.Text.Json;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Output;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Hosting.Catalyst.Tests;

public class IdentityTokenResolutionTests
{
    private static CliIdentityDetails Load(string fixture)
        => JsonSerializer.Deserialize<CliIdentityDetails>(
               File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture)))!;

    [Theory]
    [InlineData("app-get.json")]
    [InlineData("agent-get.json")]
    [InlineData("appid-get.json")]
    public void Resolves_the_api_token_for_every_identity_shape(string fixture)
        => Assert.Equal("diagrid://mock-api-token", Load(fixture).ResolveApiToken("demo", IdentityKind.App, "https://api.r1.diagrid.io/"));

    [Fact]
    public void Throws_naming_the_resource_and_context_when_no_token_path_is_present()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Load("agent-get-no-token.json").ResolveApiToken("my-agent", IdentityKind.Agent, "https://api.self-hosted.example/"));

        Assert.Contains("my-agent", ex.Message);
        Assert.Contains("https://api.self-hosted.example/", ex.Message);
        Assert.Contains("Agent API may be unavailable", ex.Message);
        Assert.DoesNotContain("appid get", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagrid appid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Agent_api_hint_is_only_given_for_agents()
    {
        var app = Assert.Throws<InvalidOperationException>(
            () => Parse("{}").ResolveApiToken("my-app", IdentityKind.App, "project 'p'"));

        Assert.Contains("my-app", app.Message);
        Assert.Contains("project 'p'", app.Message);
        Assert.DoesNotContain("Agent API", app.Message);
    }

    private static CliIdentityDetails Parse(string json)
        => JsonSerializer.Deserialize<CliIdentityDetails>(json)!;

    [Fact]
    public void Top_level_token_wins_over_the_nested_one()
        => Assert.Equal("top", Parse("""
            {"status":{"apiToken":"top","appIds":[{"status":{"apiToken":"nested"}}]}}
            """).ResolveApiToken("demo", IdentityKind.App, "https://api.example/"));

    [Fact]
    public void Empty_top_level_token_falls_back_to_the_nested_one()
        => Assert.Equal("nested", Parse("""
            {"status":{"apiToken":"","appIds":[{"status":{"apiToken":"nested"}}]}}
            """).ResolveApiToken("demo", IdentityKind.App, "https://api.example/"));

    [Fact]
    public void Empty_tokens_at_both_paths_throw()
        => Assert.Throws<InvalidOperationException>(() => Parse("""
            {"status":{"apiToken":"","appIds":[{"status":{"apiToken":""}}]}}
            """).ResolveApiToken("demo", IdentityKind.App, "https://api.example/"));

    [Theory]
    [InlineData("""{"status":{"appIds":[]}}""")]
    [InlineData("""{"status":{"appIds":null}}""")]
    [InlineData("""{"status":{"appIds":[null]}}""")]
    [InlineData("""{"status":{"appIds":[{}]}}""")]
    [InlineData("""{"status":{"appIds":[{"status":null}]}}""")]
    [InlineData("""{"status":null}""")]
    [InlineData("""{}""")]
    public void Omitted_or_null_fields_throw_a_clean_InvalidOperationException(string json)
        => Assert.Throws<InvalidOperationException>(
            () => Parse(json).ResolveApiToken("demo", IdentityKind.App, "https://api.example/"));
}
