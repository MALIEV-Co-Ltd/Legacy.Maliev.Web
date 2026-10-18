namespace Legacy.Maliev.Web.Tests;

public sealed class ServicePresentationCssParityTests
{
    [Fact]
    public void ServiceStylesCoverDirectoryCtasAndInjectionPresentation()
    {
        var root = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "application-shell.css"));
        var services = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css"));

        var eyebrowRule = System.Text.RegularExpressions.Regex.Match(
            services, @"(?m)^\.service-eyebrow\s*\{(?<body>[^}]*)\}");
        Assert.True(eyebrowRule.Success);
        Assert.DoesNotContain("font-weight", eyebrowRule.Groups["body"].Value, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(".service-index-grid", shell, StringComparison.Ordinal);
        Assert.Contains(".service-index-card-media", shell, StringComparison.Ordinal);
        Assert.Contains(".service-section-light-cta", services, StringComparison.Ordinal);
        Assert.Contains(".service-location-section .service-actions", services, StringComparison.Ordinal);
        Assert.Contains(".injection-gallery-grid", services, StringComparison.Ordinal);
        Assert.Contains(".injection-volume-grid", services, StringComparison.Ordinal);

        var landing = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "landing.css"));
        Assert.Contains(".landing-service-card--injection > img { aspect-ratio: 16 / 9; object-fit: cover; object-position: 70% center; padding: 0;", landing, StringComparison.Ordinal);
        Assert.Contains(".landing-section + .landing-section { padding-top: clamp(5.5rem, 8vw, 8rem); }", landing, StringComparison.Ordinal);
        Assert.Contains(".landing-service-card a { display: inline-flex; align-items: center; gap: .6rem;", landing, StringComparison.Ordinal);
        Assert.Contains(".injection-hero { min-height: 0; display: grid; align-items: stretch; }", services, StringComparison.Ordinal);
        Assert.Contains(".injection-benefit-grid { align-items: stretch; }", services, StringComparison.Ordinal);
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
