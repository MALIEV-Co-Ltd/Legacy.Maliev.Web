namespace Legacy.Maliev.Web.Tests;

public sealed class MemberDashboardSurfaceContractTests
{
    [Fact]
    public void MemberDashboardAndAccountRoutes_UseAccessibleAuthenticatedShell()
    {
        var root = FindRepositoryRoot();
        var pages = Path.Combine(root, "Legacy.Maliev.Web", "Areas", "Member", "Pages");
        var dashboard = Path.Combine(pages, "Index.cshtml");
        var dashboardModel = Path.Combine(pages, "Index.cshtml.cs");
        var account = Path.Combine(pages, "Account", "Index.cshtml");
        var accountModel = Path.Combine(pages, "Account", "Index.cshtml.cs");
        var layout = Path.Combine(pages, "Shared", "_LayoutMember.cshtml");
        var navigation = Path.Combine(pages, "Shared", "_MemberNavigation.cshtml");

        Assert.True(File.Exists(dashboard));
        Assert.True(File.Exists(dashboardModel));
        Assert.True(File.Exists(account));
        Assert.True(File.Exists(accountModel));
        Assert.True(File.Exists(layout));
        Assert.True(File.Exists(navigation));

        var dashboardSource = File.ReadAllText(dashboardModel);
        Assert.Contains("[Authorize]", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("GetCustomerDatabaseIdAsync", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("ICustomerAccountClient", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("ICustomerOrderClient", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("ICustomerQuotationClient", dashboardSource, StringComparison.Ordinal);

        var viewStart = File.ReadAllText(Path.Combine(pages, "_ViewStart.cshtml"));
        Assert.Contains("_LayoutMember.cshtml", viewStart, StringComparison.Ordinal);

        var layoutSource = File.ReadAllText(layout);
        var navigationSource = File.ReadAllText(navigation);
        Assert.Contains("href=\"#member-main-content\"", layoutSource, StringComparison.Ordinal);
        Assert.Contains("<main id=\"member-main-content\"", layoutSource, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"member-navigation\"", layoutSource, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Escape'", layoutSource, StringComparison.Ordinal);
        Assert.Contains("<nav", navigationSource, StringComparison.Ordinal);
        Assert.Contains("aria-label", navigationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserManager", navigationSource, StringComparison.Ordinal);

        var combined = string.Join('\n', dashboardSource, layoutSource, navigationSource);
        Assert.DoesNotContain("access_token", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbContext", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paypal", combined, StringComparison.OrdinalIgnoreCase);
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
