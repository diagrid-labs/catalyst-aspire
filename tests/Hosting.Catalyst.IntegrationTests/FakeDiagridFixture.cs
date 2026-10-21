using System.Diagnostics;
using System.Text.Json;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

public sealed record FakeRunResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Owns a temp directory containing the fake <c>diagrid</c> executable. Tests prepend
/// <see cref="BinDirectory"/> to PATH for the child process under test.
/// </summary>
public sealed class FakeDiagridFixture : IDisposable
{
    private readonly string _root;
    private readonly string _scenarioPath;
    private readonly string _recordPath;

    public FakeDiagridFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "fake-diagrid-" + Guid.NewGuid().ToString("N"));
        BinDirectory = Path.Combine(_root, "bin");
        Directory.CreateDirectory(BinDirectory);
        _scenarioPath = Path.Combine(_root, "scenario.json");
        _recordPath = Path.Combine(_root, "record.jsonl");

        // The FakeDiagrid project reference copies diagrid(.exe), diagrid.dll, runtimeconfig and deps beside the tests.
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "diagrid*"))
        {
            var dest = Path.Combine(BinDirectory, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            if (!OperatingSystem.IsWindows() && Path.GetExtension(file) == string.Empty)
            {
                File.SetUnixFileMode(dest, File.GetUnixFileMode(dest) | UnixFileMode.UserExecute);
            }
        }

        SetScenario(new Scenario());
    }

    public string BinDirectory { get; }

    public string ScenarioPath => _scenarioPath;

    public string RecordPath => _recordPath;

    public void SetScenario(Scenario s) => File.WriteAllText(_scenarioPath, JsonSerializer.Serialize(s));

    public IReadOnlyList<string[]> Invocations =>
        File.Exists(_recordPath)
            ? File.ReadAllLines(_recordPath)
                .Where(l => l.Length > 0)
                .Select(l => JsonSerializer.Deserialize<string[]>(l)!)
                .ToList()
            : [];

    public void Reset()
    {
        if (File.Exists(_recordPath)) File.Delete(_recordPath);
        SetScenario(new Scenario());
    }

    /// <summary>Applies the fake's PATH and environment to a process start info.</summary>
    public void Configure(ProcessStartInfo psi)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        psi.Environment["PATH"] = BinDirectory + Path.PathSeparator + path;
        psi.Environment["FAKE_DIAGRID_SCENARIO"] = _scenarioPath;
        psi.Environment["FAKE_DIAGRID_RECORD"] = _recordPath;
    }

    /// <summary>Runs <c>diagrid</c> by bare name so it is resolved through PATH.</summary>
    public async Task<FakeRunResult> RunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("diagrid")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Configure(psi);

        using var process = Process.Start(psi)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new FakeRunResult(process.ExitCode, await stdout, await stderr);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
