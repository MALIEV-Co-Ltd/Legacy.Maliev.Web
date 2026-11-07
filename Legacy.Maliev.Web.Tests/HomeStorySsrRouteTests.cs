using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class HomeStorySsrRouteTests(StartupDataProtectionMigrationFixture fixture) : IClassFixture<StartupDataProtectionMigrationFixture>
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task HomeStories_RenderSourceAssemblyAndCulturePreservingLinks(string culture, bool active)
    {
        await using var ownedHost = fixture.CreateFactory("Production");
        await using var host = ownedHost.WithWebHostBuilder(builder => builder
            .UseSetting("BlazorRouting:Home", active ? "true" : "false"));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        await AssertProductionAsync(client, culture, active);
    }

    internal static async Task AssertProductionAsync(HttpClient client, string culture, bool active)
    {
        using var response = await client.GetAsync($"/?culture={culture}&hero=partnership&preview=1");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Regex.Matches(html, "<h1\\b", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Contains("class=\"home-hero-option hero-assembly\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-home-hero-carousel", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hero-partnership", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hero-journey", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hero previews", html, StringComparison.Ordinal);
        foreach (var path in new[] { "/Contact", "/InstantQuotation/3D-Printing", "/Services/Index" })
        {
            var expectedHref = path == "/Services/Index"
                ? culture == "en" ? "/services?culture=en" : "/services"
                : $"{path}?culture={culture}";
            Assert.Contains($"href=\"{expectedHref}\"", html, StringComparison.Ordinal);
        }
        Assert.Contains("href=\"/About\"", html, StringComparison.Ordinal);
        var directoryLink = Assert.Single(Regex.Matches(html,
            "<a\\b(?=[^>]*\\bclass=\"[^\"]*\\blanding-service-card--directory-link\\b[^\"]*\")[^>]*\\bhref=\"(?<href>[^\"]+)\"[^>]*>[\\s\\S]*?</a>",
            RegexOptions.CultureInvariant).Cast<Match>());
        var directoryHref = directoryLink.Groups["href"].Value;
        Assert.Equal(culture == "en" ? "/services?culture=en" : "/services", directoryHref);
        Assert.Contains(culture == "th" ? "ดูบริการทั้งหมด" : "View all services", directoryLink.Value, StringComparison.Ordinal);
        using var servicesResponse = await client.GetAsync(directoryHref);
        var services = WebUtility.HtmlDecode(await servicesResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, servicesResponse.StatusCode);
        Assert.Equal("/services", servicesResponse.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Equal(culture == "en" ? "?culture=en" : string.Empty, servicesResponse.RequestMessage?.RequestUri?.Query);
        Assert.Contains($"<html lang=\"{culture}\"", services, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "<title>บริการผลิตชิ้นส่วน | MALIEV</title>" : "<title>Manufacturing services | MALIEV</title>", services, StringComparison.Ordinal);
        Assert.Contains("data-service-finder", services, StringComparison.Ordinal);
        Assert.Contains("class=\"maliev-page maliev-page-main services-index-page\"", services, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"service-breadcrumb\"", services, StringComparison.Ordinal);
        Assert.Equal(active, services.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        Assert.Contains(culture == "th" ? "ภาพประกอบ" : "Illustrative", html, StringComparison.Ordinal);
        Assert.Contains("data-tech-leader=\"resin\" data-target-x=\"790\" data-target-y=\"289\"", html, StringComparison.Ordinal);

        foreach (var key in new[] { "fdm", "resin", "nylon", "cnc" })
        {
            Assert.Matches("<path[^>]*data-tech-leader=\\\"" + key + "\\\"[^>]*pathLength=\\\"1\\\"", html);
            Assert.Matches("<circle[^>]*data-tech-point=\\\"" + key + "\\\"", html);
        }
        Assert.DoesNotContain("blazor.web.js", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("assembly", "profile-hybrid-gripper-cutout.webp", true, "en")]
    [InlineData("partnership", "camera-bracket-deployment.webp", true, "en")]
    [InlineData("journey", "camera-bracket-sketch-v6.webp", false, "en")]
    [InlineData("assembly", "profile-hybrid-gripper-cutout.webp", true, "th")]
    [InlineData("partnership", "camera-bracket-deployment.webp", true, "th")]
    [InlineData("journey", "camera-bracket-sketch-v6.webp", false, "th")]
    public async Task DevelopmentPreview_IsAllowlistedAndPreloadsItsActualHero(string hero, string image, bool hasPrimaryContact, string culture)
    {
        await using var host = fixture.CreateFactory("Development");
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await AssertPreviewAsync(client, hero, image, hasPrimaryContact, culture);
    }

    internal static async Task AssertPreviewAsync(HttpClient client, string hero, string image, bool hasPrimaryContact, string culture)
    {
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/?culture={culture}&hero={hero}&preview=1"));
        Assert.Contains(culture == "th" ? "เปรียบเทียบส่วนแรกของหน้า" : "Hero previews", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-home-hero-carousel", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<h1\\b", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Contains($"rel=\"preload\" as=\"image\" href=\"/src/images/landing/{image}\"", html, StringComparison.Ordinal);
        if (culture == "en")
            Assert.Contains(hasPrimaryContact ? "Discuss your" : "Start with your idea", html, StringComparison.Ordinal);
        else
            Assert.Contains("href=\"/Contact?culture=th\"", html, StringComparison.Ordinal);
        if (hero is "partnership" or "journey")
            Assert.Contains($"href=\"/About?culture={culture}\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown", "1")]
    [InlineData("partnership", "0")]
    public async Task InvalidDevelopmentPreview_FallsBackToProductionAssembly(string hero, string preview)
    {
        await using var host = fixture.CreateFactory("Development");
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var html = await client.GetStringAsync($"/?culture=en&hero={hero}&preview={preview}");
        Assert.Contains("hero-assembly", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-home-hero-carousel", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hero previews", html, StringComparison.Ordinal);
    }
}

public sealed class HomeStorySyntheticRouteTests(TestingWebApplicationFactory factory) : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task Production_RetainsOrderedHomeAndLocalizedLinks(string culture, bool active)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder
            .UseSetting("BlazorRouting:Home", active ? "true" : "false"));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await HomeStorySsrRouteTests.AssertProductionAsync(client, culture, active);
    }

}
