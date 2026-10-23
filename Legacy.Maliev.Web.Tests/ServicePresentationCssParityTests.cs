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
        Assert.Contains("gap: clamp(1.15rem, 2.2vw, 2rem);", shell, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", shell, StringComparison.Ordinal);
        Assert.Contains("height: clamp(11.5rem, 16vw, 14rem);", shell, StringComparison.Ordinal);
        Assert.Contains(".service-section-light-cta", services, StringComparison.Ordinal);
        Assert.Contains(".service-location-section .service-actions", services, StringComparison.Ordinal);
        Assert.Contains(".injection-gallery-grid", services, StringComparison.Ordinal);
        Assert.Contains(".injection-volume-grid", services, StringComparison.Ordinal);

        Assert.Contains(".service-material-controls {", services, StringComparison.Ordinal);
        Assert.Contains(".service-material-compare {", services, StringComparison.Ordinal);
        Assert.Contains(".service-material-compare thead th { position: sticky;", services, StringComparison.Ordinal);
        Assert.Contains(".service-table-wrap { overflow-x: auto;", services, StringComparison.Ordinal);
        Assert.Contains(".service-material-compare tbody td::before { content: attr(data-label);", services, StringComparison.Ordinal);
        Assert.Contains(".service-material-controls input:focus-visible, .service-material-controls select:focus-visible, .service-material-reset:focus-visible", services, StringComparison.Ordinal);

        var landing = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "landing.css"));
        Assert.Contains(".landing-service-card--injection > img { aspect-ratio: 16 / 9; object-fit: cover; object-position: 70% center; padding: 0;", landing, StringComparison.Ordinal);
        Assert.Contains(".landing-section + .landing-section { padding-top: clamp(5.5rem, 8vw, 8rem); }", landing, StringComparison.Ordinal);
        Assert.Contains(".landing-service-card a { display: inline-flex; align-items: center; gap: .6rem;", landing, StringComparison.Ordinal);

        Assert.Contains(".landing-service-card--directory { grid-column: span 2;", landing, StringComparison.Ordinal);
        Assert.Contains("    .landing-service-card--directory { grid-column: auto; }", landing, StringComparison.Ordinal);
        Assert.Contains(".landing-service-card > .landing-service-card--directory-link { display: grid; grid-template-columns: minmax(0, 1.05fr) minmax(12rem, .95fr);", landing, StringComparison.Ordinal);
        Assert.Contains(".landing-service-card--directory-link:focus-visible", landing, StringComparison.Ordinal);
        Assert.Contains(".landing-service-directory-preview { container-type: inline-size;", landing, StringComparison.Ordinal);
        Assert.Contains("@container (min-width: 20rem)", landing, StringComparison.Ordinal);
        Assert.Contains(".landing-service-directory-preview { grid-column: 1; grid-row: 2;", landing, StringComparison.Ordinal);
        Assert.Matches(@"@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{[\s\S]*?\.landing-service-directory-cta[^}]*transition:\s*none", landing);
        var location = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Shared", "ServiceLocation.razor"));
        Assert.Contains("SocialNetworks.GoogleMaps", location, StringComparison.Ordinal);
        Assert.Contains("SocialNetworks.Line", location, StringComparison.Ordinal);
        Assert.Contains("SocialNetworks.WhatsApp", location, StringComparison.Ordinal);
        Assert.Contains("class=\"fas fa-map-marked-alt\" aria-hidden=\"true\"", location, StringComparison.Ordinal);
        Assert.Contains("class=\"fab fa-line\" aria-hidden=\"true\"", location, StringComparison.Ordinal);
        Assert.Contains("class=\"fab fa-whatsapp\" aria-hidden=\"true\"", location, StringComparison.Ordinal);
        Assert.Contains("class=\"fas fa-file-upload\" aria-hidden=\"true\"", location, StringComparison.Ordinal);
        Assert.Contains(".service-quick { position: relative; z-index: 2; margin-top: -3.5rem; background: transparent; }", services, StringComparison.Ordinal);
        Assert.Contains(".injection-molding-page .service-quick { margin-top: -3.5rem; }", services, StringComparison.Ordinal);
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
