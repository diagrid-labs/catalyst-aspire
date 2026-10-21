using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
[Collection(FakeDiagridCollection.Name)]
public class ProcessRunnerTests
{
    private static readonly string OneKb = new string('x', 1023) + "\n";

    private static Scenario Single(ScenarioResponse response) => new() { Responses = [response] };

    private static Task<CliResult> Run(IReadOnlyList<string> args, TimeSpan? timeout = null) =>
        ProcessRunner.RunAsync(args, timeout ?? TimeSpan.FromSeconds(20), CancellationToken.None);

    [Fact(Timeout = 30_000)]
    public async Task Does_not_deadlock_when_both_streams_exceed_the_pipe_buffer()
    {
        using var fake = new FakeDiagridFixture();
        // Each stream exceeds 1 MB; the fake writes all of stdout before any stderr.
        fake.SetScenario(Single(new() { ExitCode = 0, StdOut = OneKb, StdOutRepeat = 1024, StdErr = OneKb, StdErrRepeat = 1024 }));

        var result = await Run(["project", "get", "demo"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.OutputTruncated);
        Assert.True(result.StdOut.Length >= 1024 * 1024);
        Assert.True(result.StdErr.Length >= 1024 * 1024);
    }

    [Fact(Timeout = 30_000)]
    public async Task Does_not_deadlock_when_stderr_is_written_before_stdout()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Single(new() { ExitCode = 0, StdErrFirst = true, StdOut = OneKb, StdOutRepeat = 1024, StdErr = OneKb, StdErrRepeat = 1024 }));

        var result = await Run(["project", "get", "demo"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.OutputTruncated);
        Assert.True(result.StdOut.Length >= 1024 * 1024);
        Assert.True(result.StdErr.Length >= 1024 * 1024);
    }

    [Fact(Timeout = 30_000)]
    public async Task Returns_partial_output_when_a_background_child_keeps_the_pipes_open_after_exit()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Single(new() { ExitCode = 0, StdOut = "done\n", LeakBackgroundChildMilliseconds = 15_000 }));

        var started = DateTime.UtcNow;
        var result = await ProcessRunner.RunAsync(["project", "get", "demo"], TimeSpan.FromSeconds(20), CancellationToken.None, TimeSpan.FromSeconds(3));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.StdOut.Trim());
        Assert.True(result.OutputTruncated);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5.5), "drain must be bounded by one shared grace period (3s), not one per stream");
    }

    [Fact(Timeout = 30_000)]
    public async Task Times_out_and_names_the_command_when_the_cli_hangs()
    {
        using var fake = new FakeDiagridFixture();
        // The runner closes stdin, so a stdin-reading fake would see EOF and exit; hang by sleeping instead.
        fake.SetScenario(Single(new() { SleepMilliseconds = 120_000 }));

        var ex = await Assert.ThrowsAsync<CatalystCliException>(
            () => Run(["project", "get", "demo"], TimeSpan.FromSeconds(2)));

        Assert.Contains("project get", ex.Message);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 30_000)]
    public async Task Closes_stdin_so_a_cli_that_reads_it_sees_EOF_and_exits()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Single(new() { ExitCode = 0, ReadStdInForever = true }));

        var result = await Run(["project", "get", "demo"], TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Passes_each_argument_as_one_argv_entry_including_spaces_and_quotes()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Single(new() { ExitCode = 0 }));

        await Run(["component", "create", "c", "--metadata", "connectionString=Host=x; Port=5432", "say \"hi\""]);

        Assert.Equal(
            ["component", "create", "c", "--metadata", "connectionString=Host=x; Port=5432", "say \"hi\"", "--no-color"],
            fake.Invocations.Single());
    }

    [Fact]
    public async Task Always_passes_no_color()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(Single(new() { ExitCode = 0 }));

        await Run(["project", "get", "demo"]);

        Assert.Contains("--no-color", fake.Invocations.Single());
    }
}
