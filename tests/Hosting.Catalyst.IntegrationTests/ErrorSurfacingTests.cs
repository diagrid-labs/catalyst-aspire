using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Hosting.Catalyst.Cli.Options;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

// Every test builds a FakeDiagridFixture before spawning: a bare "diagrid" must never reach the real CLI.
[Collection(FakeDiagridCollection.Name)]
public class ErrorSurfacingTests
{
    [Fact(Timeout = 30_000)]
    public async Task Failure_message_contains_stderr_text_without_escape_codes_or_update_notice()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses =
            [
                new()
                {
                    ExitCode = 1,
                    StdOut = "",
                    StdErr = "A new release of diagrid is available: 1.105.0 -> 1.106.0\n\u001b[31mError: region nope not found\u001b[0m\n",
                },
            ],
        });

        var ex = await Assert.ThrowsAsync<CatalystCliException>(
            () => Commands.CreateProject("demo", new() { Region = "nope" }, default));

        Assert.Contains("region nope not found", ex.Message);
        // Ordinal: a culture-sensitive search treats ESC as ignorable and "finds" it in any string.
        Assert.DoesNotContain("\u001b", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("new release", ex.Message);
        Assert.Contains("project create", ex.Message);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task Provisioner_CreateProject_propagates_a_cli_failure_instead_of_swallowing_it()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses = [new() { ExitCode = 1, StdErr = "Error: configured maximum number of projects reached\n" }],
        });

        var ex = await Assert.ThrowsAsync<CatalystCliException>(
            () => new CliCatalystProvisioner().CreateProject("demo", new(), default));

        Assert.Contains("maximum number of projects", ex.Message);
    }

    [Fact(Timeout = 30_000)]
    public async Task Already_exists_failure_is_no_longer_tolerated()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses = [new() { ExitCode = 1, StdOut = "project demo already exists", StdErr = "Error: already exists" }],
        });

        await Assert.ThrowsAsync<CatalystCliException>(
            () => Commands.CreateProject("demo", new(), default));
    }

    [Fact(Timeout = 30_000)]
    public async Task Successful_run_with_update_notice_on_stderr_does_not_throw()
    {
        using var fake = new FakeDiagridFixture();
        fake.SetScenario(new Scenario
        {
            Responses = [new() { ExitCode = 0, StdErr = "A new release of diagrid is available: 1 -> 2\n" }],
        });

        await Commands.CreateProject("demo", new(), default);
    }
}
