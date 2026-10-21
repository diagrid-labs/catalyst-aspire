using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Ext = Diagrid.Aspire.Hosting.Catalyst.ResourceBuilderExtensions;

namespace Hosting.Catalyst.Tests;

// Expected values are what the Diagrid CLI's ProductConsoleURL produces for product "catalyst".
public class DashboardUrlTests
{
    [Theory]
    [InlineData(null, "https://catalyst.diagrid.io/admin/organization")]
    [InlineData("", "https://catalyst.diagrid.io/admin/organization")]
    [InlineData("https://api.r1.diagrid.io/", "https://catalyst.diagrid.io/admin/organization")]
    [InlineData("https://api.self-hosted.example/", "https://catalyst.diagrid.io/admin/organization")]
    [InlineData("https://api.staging.diagrid.dev/", "https://catalyst.staging.diagrid.dev/admin/organization")]
    [InlineData("https://api.r1.dev.diagrid.io/", "https://catalyst.dev.diagrid.io/admin/organization")]
    [InlineData("https://api.r1.stg.diagrid.io/", "https://catalyst.stg.diagrid.io/admin/organization")]
    [InlineData("https://api.local.diagrid.io/", "https://catalyst.local.diagrid.io/admin/organization")]
    [InlineData("https://api.7f000001.nip.io/", "https://catalyst.local.diagrid.io/admin/organization")]
    public void Derives_the_console_host_the_way_the_cli_does(string? apiUrl, string expected)
        => Assert.Equal(expected, Ext.DashboardUrl(apiUrl));

    [Fact]
    public void Project_resource_carries_the_derived_dashboard_link()
    {
        var builder = DistributedApplication.CreateBuilder();
        var project = builder.AddCatalystProject("demo");

        var url = Assert.Single(project.Resource.Annotations.OfType<ResourceUrlAnnotation>());
        Assert.Equal("Catalyst Dashboard", url.DisplayText);
        Assert.Equal(
            Ext.DashboardUrl(Environment.GetEnvironmentVariable("DIAGRID_URL")),
            url.Url);
    }
}

public class ProjectNameAndWorkingDirectoryTests
{
    [Fact]
    public void Prefers_the_explicit_name()
        => Assert.Equal("mine", Ext.ResolveProjectName("mine", "Some.Namespace"));

    [Fact]
    public void Derives_the_name_from_the_entry_namespace()
        => Assert.Equal("my-app-host", Ext.ResolveProjectName(null, "My.App.Host"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Throws_naming_the_parameter_instead_of_falling_back_to_a_literal(string? ns)
    {
        var ex = Assert.Throws<ArgumentException>(() => Ext.ResolveProjectName(null, ns));
        Assert.Equal("customProjectName", ex.ParamName);
    }

    [Fact]
    public void The_dev_run_proxy_runs_in_the_apphost_directory()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddCatalystProject("demo");
        builder.AddExecutable("worker", "dotnet", ".").WithCatalystApp();

        var proxy = builder.Resources.OfType<ExecutableResource>().Single(r => r.Name == "worker-catalyst-proxy");

        Assert.Equal(builder.AppHostDirectory, proxy.WorkingDirectory);
        Assert.NotEqual(".", proxy.WorkingDirectory);
    }
}
