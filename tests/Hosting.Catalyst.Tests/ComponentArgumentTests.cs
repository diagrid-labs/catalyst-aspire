using System.Text.Json;
using Diagrid.Aspire.Hosting.Catalyst.Cli;
using Diagrid.Aspire.Hosting.Catalyst.Model;

namespace Hosting.Catalyst.Tests;

public class ComponentArgumentTests
{
    private static ComponentDescriptor With(string key, object? value, params string[] scopes) => new()
    {
        Name = "c",
        Type = "state.postgresql",
        Metadata = new Dictionary<string, object?> { [key] = value },
        Scopes = scopes.ToList(),
    };

    // Mutation: build the pair as two argv entries, or split on '=' and rejoin -> the whole pair is no longer one entry.
    [Fact]
    public void Keeps_an_equals_sign_inside_a_metadata_value_as_one_argument()
    {
        var args = Commands.BuildCreateComponentArgs(With("connectionString", "Host=x;Port=5432"), new() { Project = "demo" });

        var at = Array.IndexOf(args, "connectionString=Host=x;Port=5432");
        Assert.True(at > 0, "the pair must be a single argv entry");
        Assert.Equal("--metadata", args[at - 1]);
    }

    [Fact]
    public void Keeps_a_trailing_base64_padding_equals_sign()
    {
        var args = Commands.BuildUpdateComponentArgs(With("key", "QUJDRA=="), new() { Project = "demo" });

        Assert.Contains("key=QUJDRA==", args);
    }

    // Mutation: use plain ToString() -> "True". A CLR bool and a JsonElement (the runtime type from a typed spec) both covered.
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Serialises_a_boolean_metadata_value_as_lowercase(bool value, string expected)
    {
        Assert.Contains($"actorStateStore={expected}",
            Commands.BuildCreateComponentArgs(With("actorStateStore", value), new() { Project = "demo" }));
        Assert.Contains($"actorStateStore={expected}",
            Commands.BuildUpdateComponentArgs(With("actorStateStore", value), new() { Project = "demo" }));
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("\"text\"", "text")]
    [InlineData("42", "42")]
    public void Serialises_JsonElement_metadata_values_without_json_quoting_or_capitalisation(string json, string expected)
    {
        var element = JsonDocument.Parse(json).RootElement.Clone();

        Assert.Contains($"k={expected}",
            Commands.BuildUpdateComponentArgs(With("k", element), new() { Project = "demo" }));
    }

    [Fact]
    public void Update_names_the_component_and_omits_flags_update_does_not_have()
    {
        var args = Commands.BuildUpdateComponentArgs(With("k", "v", "app1", "app2"), new() { Project = "demo" });

        Assert.Equal(["component", "update", "c"], args[..3]);
        Assert.DoesNotContain("--type", args);
        Assert.DoesNotContain("--ignore-if-exists", args);
        Assert.Equal("app1,app2", args[Array.IndexOf(args, "--scopes") + 1]);
        Assert.Equal("demo", args[Array.IndexOf(args, "--project") + 1]);
        Assert.Contains("--wait", args);
    }

    // Update replaces scopes only when told; omitting --scopes would leave stale ones behind.
    [Fact]
    public void Update_with_no_declared_scopes_clears_them_with_an_empty_list()
    {
        var args = Commands.BuildUpdateComponentArgs(With("k", "v"), new() { Project = "demo" });

        Assert.Equal("[]", args[Array.IndexOf(args, "--scopes") + 1]);
    }

    [Fact]
    public void Create_with_no_declared_scopes_sends_no_scopes_flag()
        => Assert.DoesNotContain("--scopes", Commands.BuildCreateComponentArgs(With("k", "v"), new() { Project = "demo" }));

    [Fact]
    public void Update_requires_metadata()
        => Assert.Throws<ArgumentException>(() => Commands.BuildUpdateComponentArgs(
            new() { Name = "c", Type = "t", Metadata = new Dictionary<string, object?>() }, new() { Project = "demo" }));

    // Distinct keys and values, so a swapped key/value or reordered pair is visible.
    [Fact]
    public void Emits_every_metadata_pair_in_declaration_order_without_transposing_them()
    {
        var descriptor = new ComponentDescriptor
        {
            Name = "c",
            Type = "t",
            Metadata = new Dictionary<string, object?> { ["alpha"] = "one", ["beta"] = "two", ["skipped"] = null },
        };

        var args = Commands.BuildUpdateComponentArgs(descriptor, new() { Project = "demo" });

        Assert.Equal(["--metadata", "alpha=one", "--metadata", "beta=two"], args[3..7]);
        Assert.DoesNotContain(args, a => a.StartsWith("skipped", StringComparison.Ordinal));
    }
}
