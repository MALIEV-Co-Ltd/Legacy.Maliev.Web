using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class PublicServiceExpansionStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicServiceExpansionStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void ExpandedServiceRoutes_DeclareStaticSsrPagesAndRequiredPresentationAssets()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ThreeDimensionalDesign"] = ["/services/3d-design", "design-title", "/src/images/services/design/design-workflow.webp", "design-workflow", "design-faq"],
            ["SiliconeCasting"] = ["/services/silicone-casting", "silicone-title", "/src/images/services/silicone-casting/silicone-casting-workflow.webp", "silicone-workflow", "silicone-faq"],
            ["LowVolumeInjectionMolding"] = ["/services/low-volume-injection-molding", "injection-title", "/src/images/services/injection-molding/injection-service-hero-wide.webp", "injection-part-proof", "injection-workflow", "injection-faq"]
        };

        foreach (var pair in expected)
        {
            var page = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Services", $"{pair.Key}Page.razor"));
            Assert.Contains($"@page \"{pair.Value[0]}\"", page, StringComparison.Ordinal);
            Assert.Contains("RouteOwner=\"blazor-static-ssr\"", page, StringComparison.Ordinal);

            var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Services", $"{pair.Key}Content.razor"));
            foreach (var marker in pair.Value.Skip(1))
            {
                Assert.Contains(marker, content, StringComparison.Ordinal);
            }

            Assert.Contains("service-page-toc", content, StringComparison.Ordinal);
            Assert.Contains("aria-label=", content, StringComparison.Ordinal);
            Assert.Contains("service-process", content, StringComparison.Ordinal);
            Assert.Contains("data-migration-component=\"public-service-faq\"", content, StringComparison.Ordinal);
        }

        foreach (var asset in new[]
        {
            "images/services/design/design-workflow.webp",
            "images/services/design/design-inputs.webp",
            "images/services/design/design-dfm-review.webp",
            "images/services/silicone-casting/silicone-casting-workflow.webp",
            "images/services/silicone-casting/silicone-mold-prep.webp",
            "images/services/silicone-casting/silicone-pour-cure.webp",
            "images/services/injection-molding/injection-service-hero-wide.webp",
            "images/services/injection-molding/part-bento-pp-source-derived-640.webp",
            "images/services/injection-molding/part-bento-pp-source-derived-1024.webp",
            "images/services/injection-molding/part-bento-pp-source-derived.webp",
            "images/services/injection-molding/pimm-50g-controller.webp",
            "images/services/injection-molding/pimm-50g-nozzle.webp",
            "images/services/injection-molding/pimm-sample-mold.webp"
        })
        {
            Assert.True(File.Exists(Path.Combine(web, "wwwroot", "src", asset)), $"Missing service asset '{asset}'.");
        }
    }

    [Fact]
    public void HomeAndServicesDirectory_ExposeTheExpandedCatalogAndAccessibleFinderMarkup()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var home = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Home", "HomeContent.razor"));
        var services = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Services", "ServicesContent.razor"));

        foreach (var route in new[]
        {
            "/Services/Custom-Manufacturing",
            "/Services/3D-Design",
            "/Services/Silicone-Casting",
            "/Services/Low-Volume-Injection-Molding"
        })
        {
            Assert.Contains(route, home, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("landing-service-directory-preview", home, StringComparison.Ordinal);
        Assert.Contains("data-service-finder", services, StringComparison.Ordinal);
        Assert.Contains("role=\"progressbar\"", services, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"false\"", services, StringComparison.Ordinal);
        Assert.Contains("data-finder-answer", services, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(web, "wwwroot", "src", "images", "landing", "service-directory-finder.png")));
        Assert.True(File.Exists(Path.Combine(web, "wwwroot", "src", "images", "services", "custom-manufacturing", "custom-manufacturing-story.webp")));
        Assert.True(File.Exists(Path.Combine(web, "wwwroot", "src", "images", "services", "injection-molding", "pp-injection-molded-component.webp")));
    }

    [Theory]
    [InlineData("/services/3d-design", "design-title", "Design for the part")]
    [InlineData("/services/silicone-casting", "silicone-title", "Silicone Casting")]
    [InlineData("/services/low-volume-injection-molding", "injection-title", "1,000")]
    public async Task ExpandedServiceRoutes_RenderLocalizedStaticDocument(string route, string headingId, string contentMarker)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync($"{route}?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"id=\"{headingId}\"", source, StringComparison.Ordinal);
        Assert.Contains(contentMarker, source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-service-page-toc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
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
