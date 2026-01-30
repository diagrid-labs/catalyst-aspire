namespace Diagrid.Aspire.Hosting.Catalyst.Cli.Options;

public record CreateComponentOptions
{
    public required string Project { get; init; }
    public bool Wait { get; init; } = true;
}