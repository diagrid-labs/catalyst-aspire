using Diagrid.Aspire.Hosting.Catalyst.Cli;

namespace Hosting.Catalyst.Tests;

public class StdErrCleanerTests
{
    [Fact]
    public void Strips_ansi_escape_sequences()
        => Assert.Equal("Error: boom", StdErrCleaner.Clean("\u001b[31mError: boom\u001b[0m"));

    [Fact]
    public void Drops_the_update_notice_but_keeps_the_error()
    {
        var raw = "A new release of diagrid is available: 1.105.0 -> 1.106.0\nUpdate with: diagrid upgrade\nError: project not found\n";
        Assert.Equal("Error: project not found", StdErrCleaner.Clean(raw).Trim());
    }

    [Fact]
    public void Returns_empty_when_only_the_update_notice_is_present()
        => Assert.Equal("", StdErrCleaner.Clean("A new release of diagrid is available: 1.105.0 -> 1.106.0\n").Trim());

    [Fact]
    public void Drops_the_update_notice_when_it_is_wrapped_in_colour_codes_and_crlf()
    {
        var raw = "\u001b[33mA new release of diagrid is available: 1 -> 2\u001b[0m\r\nError: x\r\n";
        Assert.Equal("Error: x", StdErrCleaner.Clean(raw));
    }

    [Fact]
    public void Keeps_error_lines_that_merely_mention_updating()
        => Assert.Equal("Error: could not check for an update", StdErrCleaner.Clean("Error: could not check for an update"));
}
