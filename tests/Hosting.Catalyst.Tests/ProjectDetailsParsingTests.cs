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

    // Mutation: swapping grpc and http in ToProjectDetails fails this (the two URLs differ).
    [Fact]
    public void Reads_a_ready_project_endpoints_without_transposing_grpc_and_http()
    {
        var d = CliProjectDetails.Parse(Fixture("project-get-ready.json")).ToProjectDetails();

        Assert.NotNull(d);
        Assert.Equal(new Uri("https://http.r1.diagrid.io/v1/demo"), d.HttpEndpoint);
        Assert.Equal(new Uri("https://grpc.r1.diagrid.io:7443/demo"), d.GrpcEndpoint);
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
