using Diagrid.Aspire.Hosting.Catalyst;

namespace Hosting.Catalyst.Tests;

public class CatalystNamesTests
{
    [Theory]
    [InlineData("Test_Api")]
    [InlineData("TestApi")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("trailing\n")]
    [InlineData("")]
    [InlineData("café")]
    public void Rejects_an_aspire_name_catalyst_will_not_accept(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => CatalystNames.EnsureValidResourceName(name));
        Assert.Contains("lowercase", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"'{name}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_name_longer_than_63_characters()
    {
        var name = new string('a', 64);
        var ex = Assert.Throws<ArgumentException>(() => CatalystNames.EnsureValidResourceName(name));
        Assert.Contains(name, ex.Message, StringComparison.Ordinal);
        Assert.Contains("lowercase", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("test-api")]
    [InlineData("api2")]
    public void Accepts_valid_names(string name) => CatalystNames.EnsureValidResourceName(name);

    [Fact]
    public void Accepts_a_name_of_exactly_63_characters()
        => CatalystNames.EnsureValidResourceName(new string('a', 63));
}
