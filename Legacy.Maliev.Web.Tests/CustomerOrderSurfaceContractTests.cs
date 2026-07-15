using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerOrderSurfaceContractTests
{
    [Fact]
    public void CustomerOrderBoundary_IsRegisteredAndExposesHistoryAndViewPages()
    {
        var application = typeof(ICustomerAccountClient).Assembly;
        var infrastructure = typeof(CustomerAccountClient).Assembly;
        var repositoryRoot = FindRepositoryRoot();
        var registration = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Legacy.Maliev.Web.Infrastructure",
            "ServiceCollectionExtensions.cs"));

        Assert.NotNull(application.GetType("Legacy.Maliev.Web.Application.ICustomerOrderClient"));
        Assert.NotNull(infrastructure.GetType("Legacy.Maliev.Web.Infrastructure.CustomerOrderClient"));
        Assert.Contains("AddScoped<ICustomerOrderClient, CustomerOrderClient>()", registration, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            repositoryRoot,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Orders",
            "History.cshtml")));
        Assert.True(File.Exists(Path.Combine(
            repositoryRoot,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Orders",
            "View.cshtml")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
