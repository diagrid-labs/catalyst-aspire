using Diagrid.Aspire.Hosting.Catalyst.Cli;

namespace Hosting.Catalyst.SmokeTests;

/// <summary>Shared plumbing for tests that touch a real Catalyst account.</summary>
internal static class SmokeProject
{
    /// <summary>Every throwaway project carries this prefix; the delete path refuses anything else.</summary>
    internal const string Prefix = "aspire-smoke-";

    internal static bool Enabled
        => string.Equals(Environment.GetEnvironmentVariable("CATALYST_SMOKE_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

    internal static string? CaptureDirectory
        => Environment.GetEnvironmentVariable("CATALYST_CAPTURE_FIXTURES_DIR") is { Length: > 0 } dir ? dir : null;

    /// <summary>A unique name per call, so concurrent runs and reruns never collide. 25 characters.</summary>
    internal static string NewName() => Prefix + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Deletes a throwaway project. Never touches a project that this suite did not name itself.</summary>
    internal static async Task DeleteAsync(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to delete '{name}': not a smoke-test project.");
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var result = await ProcessRunner.RunAsync(
                ["project", "delete", name, "--wait", "--yes"], TimeSpan.FromMinutes(10), cts.Token);
            if (result.ExitCode != 0)
            {
                // Surfaced loudly so a leak is visible in the run log; the nightly sweep is the backstop.
                Console.Error.WriteLine($"LEAK WARNING: could not delete smoke project '{name}': {StdErrCleaner.Clean(result.StdErr)}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"LEAK WARNING: could not delete smoke project '{name}': {ex.Message}");
        }
    }
}
