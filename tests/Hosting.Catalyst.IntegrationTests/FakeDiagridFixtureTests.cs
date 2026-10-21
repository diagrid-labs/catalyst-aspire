using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

public class FakeDiagridFixtureTests
{
    [Fact]
    public async Task Fake_records_invocation_and_replays_scenario_response()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ArgsMatch = "project get", ExitCode = 0, StdOut = "{\"ok\":true}" }] });

        var result = await fake.RunAsync("project", "get", "demo", "--output", "json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{\"ok\":true}", result.StdOut.Trim());
        Assert.Equal(["project", "get", "demo", "--output", "json"], fake.Invocations.Single());
    }

    [Fact]
    public async Task Unmatched_invocation_exits_127_and_names_argv()
    {
        using var fake = new FakeDiagridFixture();

        var result = await fake.RunAsync("nope", "--no-color");

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("nope --no-color", result.StdErr);
        Assert.Equal(["nope", "--no-color"], fake.Invocations.Single());
    }

    [Fact]
    public async Task Null_match_matches_anything_and_repeat_emits_large_stream()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { StdOut = new string('x', 1024), StdOutRepeat = 2048, ExitCode = 3 }] });

        var result = await fake.RunAsync("anything");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(1024 * 2048, result.StdOut.Length);
    }
}
