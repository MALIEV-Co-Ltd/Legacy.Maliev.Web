namespace Legacy.Maliev.Web.Tests;

public sealed class ServicePresentationCssParityTests
{
    [Fact]
    public void ServiceStylesCoverDirectoryCtasAndInjectionPresentation()
    {
        var root = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "application-shell.css"));
        var services = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css"));

        Assert.Contains(".service-index-grid", shell, StringComparison.Ordinal);
        Assert.Contains(".service-index-card-media", shell, StringComparison.Ordinal);
        Assert.Contains(".service-section-light-cta", services, StringComparison.Ordinal);
        Assert.Contains(".service-location-section .service-actions", services, StringComparison.Ordinal);
        Assert.Contains(".injection-gallery-grid", services, StringComparison.Ordinal);
        Assert.Contains(".injection-volume-grid", services, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", services, StringComparison.Ordinal);
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
