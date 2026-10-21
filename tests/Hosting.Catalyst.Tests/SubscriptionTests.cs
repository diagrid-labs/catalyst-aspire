using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.Cli;

namespace Hosting.Catalyst.Tests;

public class SubscriptionTests
{
    private static readonly Regex DnsLabel = new(@"^[a-z0-9]([-a-z0-9]*[a-z0-9])?\z", RegexOptions.CultureInvariant);

    private static CatalystProject Build(Action<IResourceBuilder<ExecutableResource>> configure)
    {
        var builder = DistributedApplication.CreateBuilder();
        var catalyst = builder.AddCatalystProject("demo");
        var app = builder.AddExecutable("test-api", "dotnet", ".").WithCatalystApp();
        configure(app);
        return catalyst.Resource;
    }

    // Mutation: put the old constant name back ($"{resource}-pubsub") -> the second WithSubscription throws.
    [Fact]
    public void Two_topics_on_one_app_produce_distinct_subscriptions()
    {
        var project = Build(b => b.WithSubscription("orders", "/orders").WithSubscription("shipments", "/shipments"));

        Assert.Equal(2, project.Subscriptions.Count);
        Assert.Equal("/orders", project.Subscriptions["test-api-pubsub-orders"].Route);
        Assert.Equal("/shipments", project.Subscriptions["test-api-pubsub-shipments"].Route);
    }

    [Fact]
    public void The_generated_name_is_what_the_descriptor_carries_and_scopes_the_app()
    {
        var project = Build(b => b.WithSubscription("orders", "/orders"));

        var d = Assert.Single(project.Subscriptions).Value;
        Assert.Equal("test-api-pubsub-orders", d.Name);
        Assert.Equal("pubsub", d.Component);
        Assert.Equal("orders", d.Topic);
        Assert.Equal(["test-api"], d.Scopes);
    }

    [Theory]
    [InlineData("Order.Created", "test-api-pubsub-order-created")]
    [InlineData("orders", "test-api-pubsub-orders")]
    [InlineData("a__b..c", "test-api-pubsub-a-b-c")]
    [InlineData("-orders-", "test-api-pubsub-orders")]
    [InlineData("Ünïcode/Topic", "test-api-pubsub-n-code-topic")]
    public void Sanitises_the_topic_into_a_dns_label(string topic, string expected)
    {
        var name = CatalystNames.SubscriptionName("test-api", "pubsub", topic);

        Assert.Equal(expected, name);
        Assert.Matches(DnsLabel, name);
    }

    // Mutation: swap two arguments in the format string -> the distinct values land in the wrong order.
    [Fact]
    public void Resource_component_and_topic_appear_in_that_order()
        => Assert.Equal("web-events-orders", CatalystNames.SubscriptionName("web", "events", "orders"));

    [Fact]
    public void A_name_over_63_characters_is_truncated_with_a_stable_hash_and_stays_a_dns_label()
    {
        var topic1 = new string('t', 80) + "-one";
        var topic2 = new string('t', 80) + "-two";

        var a = CatalystNames.SubscriptionName("test-api", "pubsub", topic1);
        var again = CatalystNames.SubscriptionName("test-api", "pubsub", topic1);
        var b = CatalystNames.SubscriptionName("test-api", "pubsub", topic2);

        Assert.True(a.Length <= 63);
        Assert.Equal(a, again);
        Assert.NotEqual(a, b);
        Assert.Matches(DnsLabel, a);
        Assert.StartsWith("test-api-pubsub-ttt", a, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncation_never_leaves_a_trailing_dash_before_the_hash()
    {
        // Index 53 of the sanitised label is a dash, the last character kept by the cut.
        var topic = new string('t', 37) + "-" + new string('u', 40);
        var name = CatalystNames.SubscriptionName("test-api", "pubsub", topic);

        Assert.DoesNotContain("--", name, StringComparison.Ordinal);
        Assert.Matches(DnsLabel, name);
        Assert.True(name.Length <= 63);
    }

    [Fact]
    public void A_name_of_exactly_63_characters_is_kept_whole()
    {
        var topic = new string('t', 63 - "test-api-pubsub-".Length);
        var name = CatalystNames.SubscriptionName("test-api", "pubsub", topic);

        Assert.Equal($"test-api-pubsub-{topic}", name);
        Assert.Equal(63, name.Length);
    }

    // Mutation: drop the duplicate check -> Dictionary.Add still throws, but without naming all three; so assert each.
    [Fact]
    public void A_true_duplicate_still_throws_naming_app_component_and_topic()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Build(b => b.WithSubscription("orders", "/a").WithSubscription("orders", "/b")));

        Assert.Contains("'test-api'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'pubsub'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'orders'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_through_the_full_overload_names_all_three_too()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Build(b => b.WithSubscription("s1", "events", "orders", "/a").WithSubscription("s1", "events", "orders", "/b")));

        Assert.Contains("'test-api'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'events'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'orders'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_different_topics_that_sanitise_to_the_same_name_are_reported_as_a_name_clash_not_a_duplicate()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Build(b => b.WithSubscription("order.created", "/a").WithSubscription("order-created", "/b")));

        Assert.Contains("order.created", ex.Message, StringComparison.Ordinal);
        Assert.Contains("order-created", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("already subscribes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bulk_overload_carries_its_options_to_the_descriptor()
    {
        var project = Build(b => b.WithSubscription("s", "events", "orders", "/o", true, 100, TimeSpan.FromSeconds(2)));

        var d = Assert.Single(project.Subscriptions).Value;
        Assert.True(d.Bulk);
        Assert.Equal(100, d.BulkMaxMessagesCount);
        Assert.Equal(TimeSpan.FromSeconds(2), d.BulkMaxAwaitDuration);
    }

    // Mutation: stop emitting the bulk flags in BuildCreateSubscriptionArgs.
    [Fact]
    public void Create_passes_bulk_options_through_with_a_go_duration()
    {
        var args = Commands.BuildCreateSubscriptionArgs("s", Options(bulk: true, count: 100, awaitFor: TimeSpan.FromSeconds(2)));

        Assert.Contains("--bulk", args);
        Assert.Equal("100", args[Array.IndexOf(args, "--bulk-max-messages-count") + 1]);
        Assert.Equal("2s", args[Array.IndexOf(args, "--bulk-max-await-duration") + 1]);
    }

    [Fact]
    public void Create_omits_bulk_options_when_not_declared()
    {
        var args = Commands.BuildCreateSubscriptionArgs("s", Options());

        Assert.DoesNotContain("--bulk", args);
        Assert.DoesNotContain("--bulk-max-messages-count", args);
        Assert.DoesNotContain("--bulk-max-await-duration", args);
    }

    [Fact]
    public void Update_sends_the_declared_values_wholesale_with_distinct_values_per_flag()
    {
        var args = Commands.BuildUpdateSubscriptionArgs("sub-name",
            Options(bulk: true, count: 7, awaitFor: TimeSpan.FromSeconds(90)));

        Assert.Equal(["subscription", "update", "sub-name"], args.Take(3));
        Assert.Equal("events", args[Array.IndexOf(args, "--component") + 1]);
        Assert.Equal("orders", args[Array.IndexOf(args, "--topic") + 1]);
        Assert.Equal("/route", args[Array.IndexOf(args, "--route") + 1]);
        Assert.Equal("web,worker", args[Array.IndexOf(args, "--scopes") + 1]);
        Assert.Contains("--bulk=true", args);
        Assert.Equal("7", args[Array.IndexOf(args, "--bulk-max-messages-count") + 1]);
        Assert.Equal("1m30s", args[Array.IndexOf(args, "--bulk-max-await-duration") + 1]);
        Assert.Equal("demo", args[Array.IndexOf(args, "--project") + 1]);
        Assert.DoesNotContain("--ignore-if-exists", args);
    }

    [Fact]
    public void Update_turns_bulk_off_explicitly_and_clears_empty_scopes()
    {
        var args = Commands.BuildUpdateSubscriptionArgs("s", Options(scopes: []));

        Assert.Contains("--bulk=false", args);
        Assert.DoesNotContain("--bulk", args);
        Assert.Equal("[]", args[Array.IndexOf(args, "--scopes") + 1]);
    }

    [Fact]
    public void Get_is_a_project_scoped_probe()
        => Assert.Equal(["subscription", "get", "s", "--project", "demo"], Commands.BuildGetSubscriptionArgs("s", "demo"));

    [Theory]
    [InlineData(2, "2s")]
    [InlineData(90, "1m30s")]
    [InlineData(60, "1m")]
    [InlineData(3600, "1h")]
    [InlineData(3661, "1h1m1s")]
    [InlineData(0, "0s")]
    public void Formats_durations_the_way_go_parses_them(int seconds, string expected)
        => Assert.Equal(expected, Commands.FormatGoDuration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(250, "0.25s")]
    [InlineData(1500, "1.5s")]
    [InlineData(1, "0.001s")]
    public void Formats_sub_second_durations_as_fractional_seconds(int milliseconds, string expected)
        => Assert.Equal(expected, Commands.FormatGoDuration(TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void Rejects_a_negative_duration()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Commands.FormatGoDuration(TimeSpan.FromSeconds(-1)));

    private static Diagrid.Aspire.Hosting.Catalyst.Cli.Options.CreateSubscriptionOptions Options(
        bool bulk = false, int? count = null, TimeSpan? awaitFor = null, IList<string>? scopes = null) => new()
    {
        Project = "demo",
        Component = "events",
        Topic = "orders",
        Route = "/route",
        Scopes = scopes ?? ["web", "worker"],
        Bulk = bulk,
        BulkMaxMessagesCount = count,
        BulkMaxAwaitDuration = awaitFor,
    };
}
