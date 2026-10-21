using System.Text;
using System.Text.Json;
using FakeDiagrid;

// Fake `diagrid` CLI. Scenario path: FAKE_DIAGRID_SCENARIO. Invocation log: FAKE_DIAGRID_RECORD.
// Every argument (including a trailing --no-color) is recorded verbatim.
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

var recordPath = Environment.GetEnvironmentVariable("FAKE_DIAGRID_RECORD");
if (!string.IsNullOrEmpty(recordPath))
{
    var line = JsonSerializer.Serialize(args) + "\n";
    // Retry briefly: concurrent invocations may contend for the file.
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            using var fs = new FileStream(recordPath, FileMode.Append, FileAccess.Write, FileShare.Read);
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
var response = scenario.Responses.FirstOrDefault(r => r.ArgsMatch is null || joined.Contains(r.ArgsMatch, StringComparison.Ordinal));

if (response is null)
{
    Console.Error.WriteLine($"FakeDiagrid: no scenario response matches argv: {joined}");
    return 127;
}

if (response.StdOut is not null)
{
    using var stdout = Console.OpenStandardOutput();
    var bytes = Encoding.UTF8.GetBytes(response.StdOut);
    for (var i = 0; i < response.StdOutRepeat; i++) stdout.Write(bytes);
    stdout.Flush();
}

if (response.StdErr is not null)
{
    using var stderr = Console.OpenStandardError();
    var bytes = Encoding.UTF8.GetBytes(response.StdErr);
    for (var i = 0; i < response.StdErrRepeat; i++) stderr.Write(bytes);
    stderr.Flush();
}

if (response.ReadStdInForever)
{
    using var stdin = Console.OpenStandardInput();
    var buffer = new byte[4096];
    while (stdin.Read(buffer, 0, buffer.Length) > 0) { }
}

return response.ExitCode;
