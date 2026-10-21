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
    public bool ReadStdInForever { get; init; }
}
