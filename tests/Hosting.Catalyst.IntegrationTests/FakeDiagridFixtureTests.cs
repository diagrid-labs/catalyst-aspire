using System.Diagnostics;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

[Collection(FakeDiagridCollection.Name)]
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
    public async Task Bare_diagrid_resolves_to_the_BinDirectory_copy_and_nothing_else()
    {
        // Mutation guard: if BinDirectory is not what resolves "diagrid", the record file (written only
        // by the copy owned by this fixture) stays empty, or the spawn throws. A real diagrid CLI on the
        // machine would neither record here nor exit 127 with the fake's message.
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "diagrid.exe")) || File.Exists(Path.Combine(AppContext.BaseDirectory, "diagrid")));

        using var fake = new FakeDiagridFixture();
        Assert.NotEmpty(Directory.EnumerateFiles(fake.BinDirectory, "diagrid*"));

        var result = await fake.RunAsync("who-am-i", "--no-color=true");

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("FakeDiagrid: no scenario response matches argv: who-am-i --no-color=true", result.StdErr);
        Assert.Equal(["who-am-i", "--no-color=true"], fake.Invocations.Single());
    }

    // The real CLI rejects a bare --no-color ("flag needs an argument"); the fake must too, or every test passes
    // while production fails on every call. Mutation: drop the check in the fake, or revert ProcessRunner to the bare flag.
    [Fact]
    public async Task A_bare_no_color_flag_is_a_usage_error_like_the_real_cli()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { StdOut = "ok" }] });

        var bare = await fake.RunAsync("version", "--no-color");
        var valued = await fake.RunAsync("version", "--no-color=true");

        Assert.Equal(1, bare.ExitCode);
        Assert.Contains("flag needs an argument: --no-color", bare.StdErr);
        Assert.Equal(0, valued.ExitCode);
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

    [Fact]
    public async Task First_matching_response_wins()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses =
            [
                new() { ArgsMatch = "project get", StdOut = "first", ExitCode = 1 },
                new() { ArgsMatch = "project", StdOut = "second", ExitCode = 2 },
                new() { StdOut = "third", ExitCode = 3 },
            ],
        });

        var specific = await fake.RunAsync("project", "get", "x");
        var broader = await fake.RunAsync("project", "list");
        var fallback = await fake.RunAsync("app", "list");

        Assert.Equal((1, "first"), (specific.ExitCode, specific.StdOut));
        Assert.Equal((2, "second"), (broader.ExitCode, broader.StdOut));
        Assert.Equal((3, "third"), (fallback.ExitCode, fallback.StdOut));
    }

    [Fact]
    public async Task StdErrRepeat_emits_large_stderr_stream()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { StdErr = new string('e', 1024), StdErrRepeat = 2048, ExitCode = 4 }] });

        var result = await fake.RunAsync("anything");

        Assert.Equal(4, result.ExitCode);
        Assert.Equal(1024 * 2048, result.StdErr.Length);
        Assert.Equal(string.Empty, result.StdOut);
    }

    [Fact]
    public async Task ReadStdInForever_blocks_until_stdin_closes()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { ReadStdInForever = true, ExitCode = 5 }] });

        var psi = new ProcessStartInfo("diagrid") { RedirectStandardInput = true, UseShellExecute = false };
        fake.Configure(psi);
        using var process = Process.Start(psi)!;
        try
        {
            using var shortWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.WaitForExitAsync(shortWait.Token));
            Assert.False(process.HasExited);

            process.StandardInput.Close();
            using var longWait = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(longWait.Token);
            Assert.Equal(5, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
        }
    }

    [Fact]
    public async Task Reset_clears_invocations_and_scenario()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { StdOut = "hi" }] });
        await fake.RunAsync("a");
        Assert.Single(fake.Invocations);

        fake.Reset();

        Assert.Empty(fake.Invocations);
        var result = await fake.RunAsync("a");
        Assert.Equal(127, result.ExitCode);
        Assert.Single(fake.Invocations);
    }

    [Fact]
    public async Task Concurrent_invocations_are_all_recorded()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario { Responses = [new() { StdOut = "ok" }] });

        var runs = Enumerable.Range(0, 8).Select(i => Task.Run(() => fake.RunAsync("run", i.ToString()))).ToArray();
        // Read while writers are active; must never be denied.
        while (!runs.All(r => r.IsCompleted)) _ = fake.Invocations;
        await Task.WhenAll(runs);

        Assert.All(runs, r => Assert.Equal(0, r.Result.ExitCode));
        var seen = fake.Invocations.Select(a => a[1]).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(Enumerable.Range(0, 8).Select(i => i.ToString()).OrderBy(s => s, StringComparer.Ordinal), seen);
    }
}
