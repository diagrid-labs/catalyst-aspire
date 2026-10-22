using System.Globalization;
using System.Text;
using System.Text.Json;
using FakeDiagrid;

// Fake `diagrid` CLI. Scenario path: FAKE_DIAGRID_SCENARIO. Invocation log: FAKE_DIAGRID_RECORD.
// Every argument (including a trailing --no-color) is recorded verbatim.
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

// Background-child mode (see ScenarioResponse.LeakBackgroundChildMilliseconds): hold the inherited pipes open.
var childSleep = Environment.GetEnvironmentVariable("FAKE_DIAGRID_CHILD_SLEEP_MS");
if (!string.IsNullOrEmpty(childSleep))
{
    Thread.Sleep(int.Parse(childSleep, CultureInfo.InvariantCulture));
    return 0;
}

// The real CLI registers --no-color as a flag that takes a VALUE, so a bare --no-color is a usage error. The fake
// used to accept it, which let every test pass while production would have failed on every call. Mirror the CLI.
if (args.Contains("--no-color"))
{
    Console.Error.WriteLine("Error: flag needs an argument: --no-color");
    return 1;
}

var recordPath = Environment.GetEnvironmentVariable("FAKE_DIAGRID_RECORD");
if (!string.IsNullOrEmpty(recordPath))
{
    var line = JsonSerializer.Serialize(args) + "\n";
    // FileShare.None is load-bearing on Linux: .NET seeks to the end at open (no O_APPEND) and only takes an
    // exclusive flock for FileShare.None, so with FileShare.Read two writers raced and lost lines. Contention
    // surfaces as an IOException, which the retry below absorbs.
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            using var fs = new FileStream(recordPath, FileMode.Append, FileAccess.Write, FileShare.None);
            fs.Write(new UTF8Encoding(false).GetBytes(line));
            break;
        }
        catch (IOException) when (attempt < 500)
        {
            Thread.Sleep(20);
        }
    }
}

var scenarioPath = Environment.GetEnvironmentVariable("FAKE_DIAGRID_SCENARIO");
var scenario = !string.IsNullOrEmpty(scenarioPath) && File.Exists(scenarioPath)
    ? JsonSerializer.Deserialize<Scenario>(File.ReadAllText(scenarioPath), options) ?? new Scenario()
    : new Scenario();

var joined = string.Join(' ', args);

// Invocation log lines (this call is already appended), for ScenarioResponse.Times.
// Read only when a response needs it: other fakes hold the file open for append, so every read must share
// with writers (FileShare.ReadWrite) and retry, exactly as the writer above does.
List<string> ReadRecorded()
{
    if (string.IsNullOrEmpty(recordPath))
    {
        throw new InvalidOperationException("FakeDiagrid: a scenario response uses Times but FAKE_DIAGRID_RECORD is not set.");
    }

    for (var attempt = 0; ; attempt++)
    {
        try
        {
            using var fs = new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length > 0)
                {
                    lines.Add(string.Join(' ', JsonSerializer.Deserialize<string[]>(line)!));
                }
            }
            return lines;
        }
        catch (IOException) when (attempt < 500)
        {
            Thread.Sleep(20);
        }
    }
}

var recorded = scenario.Responses.Any(r => r.Times > 0) ? ReadRecorded() : [];

bool Applies(ScenarioResponse r) =>
    (r.ArgsMatch is null || joined.Contains(r.ArgsMatch, StringComparison.Ordinal))
    && (r.Times <= 0 || recorded.Count(l => r.ArgsMatch is null || l.Contains(r.ArgsMatch, StringComparison.Ordinal)) <= r.Times);

var response = scenario.Responses.FirstOrDefault(Applies);

if (response is null)
{
    Console.Error.WriteLine($"FakeDiagrid: no scenario response matches argv: {joined}");
    return 127;
}

void WriteStdOut()
{
    if (response.StdOut is not null)
    {
        using var stdout = Console.OpenStandardOutput();
        var bytes = Encoding.UTF8.GetBytes(response.StdOut);
        for (var i = 0; i < response.StdOutRepeat; i++)
        {
            stdout.Write(bytes);
        }

        stdout.Flush();
    }
}

void WriteStdErr()
{
    if (response.StdErr is not null)
    {
        using var stderr = Console.OpenStandardError();
        var bytes = Encoding.UTF8.GetBytes(response.StdErr);
        for (var i = 0; i < response.StdErrRepeat; i++)
        {
            stderr.Write(bytes);
        }

        stderr.Flush();
    }
}

if (response.StdErrFirst) { WriteStdErr(); WriteStdOut(); } else { WriteStdOut(); WriteStdErr(); }

if (response.ReadStdInForever)
{
    using var stdin = Console.OpenStandardInput();
    var buffer = new byte[4096];
    while (stdin.Read(buffer, 0, buffer.Length) > 0) { }
}

if (response.LeakBackgroundChildMilliseconds > 0)
{
    var child = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    child.Environment["FAKE_DIAGRID_CHILD_SLEEP_MS"] = response.LeakBackgroundChildMilliseconds.ToString(CultureInfo.InvariantCulture);
    child.Environment.Remove("FAKE_DIAGRID_RECORD");
    System.Diagnostics.Process.Start(child)?.Dispose();
}

if (response.SleepMilliseconds > 0)
{
    Thread.Sleep(response.SleepMilliseconds);
}

return response.ExitCode;
