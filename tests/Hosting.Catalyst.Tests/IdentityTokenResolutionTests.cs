using System.Text.Json;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

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
        => Assert.Equal("diagrid://mock-api-token", Load(fixture).ResolveApiToken("demo", "https://api.r1.diagrid.io/"));

    [Fact]
    public void Throws_naming_the_resource_and_api_url_when_no_token_path_is_present()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Load("agent-get-no-token.json").ResolveApiToken("my-agent", "https://api.self-hosted.example/"));

        Assert.Contains("my-agent", ex.Message);
        Assert.Contains("https://api.self-hosted.example/", ex.Message);
        Assert.Contains("Agent API may be unavailable", ex.Message);
    }
}
