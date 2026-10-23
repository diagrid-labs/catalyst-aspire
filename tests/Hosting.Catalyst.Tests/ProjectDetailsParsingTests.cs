using Diagrid.Aspire.Hosting.Catalyst.Cli.Output;

namespace Hosting.Catalyst.Tests;

public class ProjectDetailsParsingTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    // Mutation: re-adding `required` to CliProjectStatus.Endpoints makes Parse throw JsonException here.
    [Fact]
    public void Reads_a_project_without_endpoints_as_not_ready_rather_than_throwing()
        => Assert.Null(CliProjectDetails.Parse(Fixture("project-get-pending.json")).ToProjectDetails());

    [Fact]
    public void The_pending_fixture_really_omits_endpoints()
    {
        // Guards the fixture: if someone adds endpoints to it, the test above stops proving anything.
        Assert.DoesNotContain("\"endpoints\"", Fixture("project-get-pending.json"), StringComparison.Ordinal);
    }

    // The URLs are what a real `project get` returns: one host per protocol, both on 443, no path. The earlier
    // hand-built fixture used a shared host with a /v1/<project> path and distinct ports, which was never the
    // shape the CLI emits. 443 is the default for https, so Uri renders it without the port and with the root
    // path -- that is Uri's doing, not the CLI's.
    // Mutation: swapping grpc and http in ToProjectDetails fails this (the two hosts differ).
    [Fact]
    public void Reads_a_ready_project_endpoints_without_transposing_grpc_and_http()
    {
        var d = CliProjectDetails.Parse(Fixture("project-get-ready.json")).ToProjectDetails();

        Assert.NotNull(d);
        Assert.Equal(new Uri("https://http-prj00000000.cloud.r1.diagrid.io:443"), d.HttpEndpoint);
        Assert.Equal(new Uri("https://grpc-prj00000000.cloud.r1.diagrid.io:443"), d.GrpcEndpoint);
    }

    [Theory]
    [InlineData("""{"metadata":{},"spec":{}}""")]
    [InlineData("""{"metadata":{},"spec":{},"status":{"endpoints":{}}}""")]
    [InlineData("""{"metadata":{},"spec":{},"status":{"endpoints":{"http":{"url":"https://h.example/v1/p","port":443}}}}""")]
    [InlineData("""{"metadata":{},"spec":{},"status":{"endpoints":{"grpc":{"url":"https://g.example:443/p","port":443}}}}""")]
    [InlineData("""{"metadata":{},"spec":{},"status":{"endpoints":{"grpc":{"port":443},"http":{"port":443}}}}""")]
    [InlineData("""{"metadata":{},"spec":{},"status":{"endpoints":{"grpc":{"url":"https://g.example:7443/p","port":7443},"http":{"url":"","port":443}}}}""")]
    [InlineData("""{"metadata":{},"spec":{},"status":{"endpoints":{"grpc":{"url":"","port":7443},"http":{"url":"https://h.example/v1/p","port":443}}}}""")]
    public void Any_missing_part_of_the_endpoints_means_not_ready(string json)
        => Assert.Null(CliProjectDetails.Parse(json).ToProjectDetails());
}
