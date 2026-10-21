namespace FakeDiagrid;

/// <summary>A scripted set of responses the fake diagrid CLI replays.</summary>
public record Scenario
{
    public IList<ScenarioResponse> Responses { get; init; } = [];
}

/// <summary>
/// One scripted response. <see cref="ArgsMatch"/> is a substring matched against the
/// space-joined argv; the first matching response wins. A null match matches anything.
/// </summary>
public record ScenarioResponse
{
    public string? ArgsMatch { get; init; }
    public int ExitCode { get; init; }
    public string? StdOut { get; init; }
    public string? StdErr { get; init; }
    public int StdOutRepeat { get; init; } = 1;
    public int StdErrRepeat { get; init; } = 1;
    /// <summary>Writes stderr before stdout (default is stdout first).</summary>
    public bool StdErrFirst { get; init; }
    /// <summary>
    /// Spawns a detached copy of the fake that inherits stdout/stderr and sleeps this long, then exits at once:
    /// the parent is gone but the pipes stay open, like a daemon an update checker left behind.
    /// </summary>
    public int LeakBackgroundChildMilliseconds { get; init; }
    public bool ReadStdInForever { get; init; }
    /// <summary>Sleeps this long before exiting, to simulate a CLI that hangs.</summary>
    public int SleepMilliseconds { get; init; }
}
