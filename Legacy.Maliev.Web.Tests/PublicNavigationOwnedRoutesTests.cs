namespace Legacy.Maliev.Web.Tests;

public sealed class PublicNavigationOwnedRoutesTests
{
    [Fact]
    public void PublicNavigationAndFooterExposeOnlyOwnedServiceRoutes()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var navigation = File.ReadAllText(Path.Combine(web, "Components", "Layout", "PublicNavigation.razor"));
        var footer = File.ReadAllText(Path.Combine(web, "Components", "Layout", "PublicFooter.razor"));

        foreach (var route in new[]
        {
            "/Services/Custom-Manufacturing",
            "/Services/3D-Design",
            "/Services/Silicone-Casting",
            "/Services/Low-Volume-Injection-Molding",
            "/Services"
        })
        {
            Assert.Contains(route, navigation, StringComparison.Ordinal);
            Assert.Contains(route, footer, StringComparison.Ordinal);
        }

        Assert.Contains("landing-services-menu", navigation, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"landing-services-menu\"", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceLocationKeepsAccessibleOwnedContactActions()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "Legacy.Maliev.Web", "Components", "Shared", "ServiceLocation.razor"));

        Assert.Contains("service-location-section", source, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"service_location_line\"", source, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"service_location_quotation\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", source, StringComparison.Ordinal);
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
