using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FakeDiagrid;

namespace Hosting.Catalyst.IntegrationTests;

public sealed record FakeRunResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Serialises every test that uses <see cref="FakeDiagridFixture"/>: it mutates process-global environment.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FakeDiagridCollection
{
    public const string Name = "FakeDiagrid";
}

/// <summary>
/// Owns a temp directory containing the fake <c>diagrid</c> executable and makes a bare
/// <c>diagrid</c> resolve to it by prepending <see cref="BinDirectory"/> to THIS process's PATH
/// (a child resolves bare names via the parent's PATH on Linux and via CreateProcess on Windows).
/// PATH, FAKE_DIAGRID_SCENARIO and FAKE_DIAGRID_RECORD are restored on Dispose. Because that is
/// process-global, test classes using it must carry <c>[Collection(FakeDiagridCollection.Name)]</c>.
/// The fake is deliberately NOT in the test output directory (which Windows searches first).
/// </summary>
public sealed class FakeDiagridFixture : IDisposable
{
    private readonly string _root;
    private readonly string _scenarioPath;
    private readonly string _recordPath;
    private readonly Dictionary<string, string?> _savedEnv = new();

    public FakeDiagridFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "fake-diagrid-" + Guid.NewGuid().ToString("N"));
        BinDirectory = Path.Combine(_root, "bin");
        Directory.CreateDirectory(BinDirectory);
        _scenarioPath = Path.Combine(_root, "scenario.json");
        _recordPath = Path.Combine(_root, "record.jsonl");

        var source = typeof(FakeDiagridFixture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "FakeDiagridDir").Value!;
        foreach (var file in Directory.EnumerateFiles(source, "diagrid*"))
        {
            var dest = Path.Combine(BinDirectory, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            if (!OperatingSystem.IsWindows() && Path.GetExtension(file) == string.Empty)
            {
                File.SetUnixFileMode(dest, File.GetUnixFileMode(dest) | UnixFileMode.UserExecute);
            }
        }

        SetScenario(new Scenario());

        SetEnv("PATH", BinDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        SetEnv("FAKE_DIAGRID_SCENARIO", _scenarioPath);
        SetEnv("FAKE_DIAGRID_RECORD", _recordPath);
    }

    public string BinDirectory { get; }

    public void SetScenario(Scenario s) => File.WriteAllText(_scenarioPath, JsonSerializer.Serialize(s));

    public IReadOnlyList<string[]> Invocations
    {
        get
        {
            // The fake takes an exclusive lock while appending (required on Linux, where .NET has no O_APPEND), so a
            // read that lands mid-write is denied: retry briefly rather than fail.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return ReadInvocations();
                }
                catch (IOException) when (attempt < 200)
                {
                    Thread.Sleep(10);
                }
            }
        }
    }

    private List<string[]> ReadInvocations()
    {
        if (!File.Exists(_recordPath)) return [];
        // FileShare.ReadWrite so a concurrently appending fake is never denied.
        using var fs = new FileStream(_recordPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        var list = new List<string[]>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 0) list.Add(JsonSerializer.Deserialize<string[]>(line)!);
        }
        return list;
    }

    public void Reset()
    {
        if (File.Exists(_recordPath)) File.Delete(_recordPath);
        SetScenario(new Scenario());
    }

    /// <summary>Applies the fake's PATH and environment to a process start info (also inherited by default).</summary>
    public void Configure(ProcessStartInfo psi)
    {
        psi.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH");
        psi.Environment["FAKE_DIAGRID_SCENARIO"] = _scenarioPath;
        psi.Environment["FAKE_DIAGRID_RECORD"] = _recordPath;
    }

    /// <summary>Runs <c>diagrid</c> by bare name so it is resolved through PATH, as production code does.</summary>
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
        foreach (var (key, value) in _savedEnv) Environment.SetEnvironmentVariable(key, value);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void SetEnv(string key, string? value)
    {
        _savedEnv[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
}
