using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class NonDisclosureAgreementStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public NonDisclosureAgreementStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheNonDisclosureAgreementRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Legal", "NonDisclosureAgreementPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Legal", "NonDisclosureAgreementContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Legal", "NonDisclosureAgreement.cshtml"));

        Assert.Contains("BlazorRouting:NonDisclosureAgreement", program, StringComparison.Ordinal);
        Assert.Contains("\"/Legal/NonDisclosureAgreement\"", program, StringComparison.Ordinal);
        Assert.Contains("\"NonDisclosureAgreement\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(NonDisclosureAgreementContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Non-Disclosure Agreement | MALIEV",
        "Your confidentiality at the Maliev is of the utmost importance.",
        "non-disclosure agreement, confidential agreement",
        "Mutual Non-disclosure agreement")]
    [InlineData(
        "th",
        "สัญญาปกปิดความลับ | MALIEV",
        "เราให้ความสำคัญกับความลับของชิ้นงานอยู่มากที่สุด คุณสามารถขอสัญญาปกปิดความลับได้ที่นี่",
        "สัญญาปกปิดความลับ",
        "สัญญาปกปิดความลับแบบสองฝ่าย")]
    public async Task NonDisclosureAgreementRoute_RendersLocalizedStaticSsrWithSeoAnalyticsAndDocumentParity(
        string culture,
        string title,
        string description,
        string keywords,
        string heading)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/legal/nondisclosureagreement?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"keywords\" content=\"{keywords}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"https://storage.googleapis.com/maliev.com/web-contents/documents/mutual%20non-disclosure%20agreement.pdf\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wowjs", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/legal/nondisclosureagreement?culture=en", "https://www.maliev.com/legal/nondisclosureagreement?culture=en", "https://www.maliev.com/legal/nondisclosureagreement")]
    [InlineData("th", "https://www.maliev.com/legal/nondisclosureagreement", "https://www.maliev.com/legal/nondisclosureagreement?culture=en", "https://www.maliev.com/legal/nondisclosureagreement")]
    public async Task NonDisclosureAgreementRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/legal/nondisclosureagreement?culture={culture}&tracking=excluded"));

        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledNonDisclosureAgreementRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:NonDisclosureAgreement", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/legal/nondisclosureagreement?culture=en"));

        Assert.Contains("<title>Non-Disclosure Agreement | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) => sourceFactory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static int CountLink(string source, string relation, string url) =>
        Regex.Matches(source, $"<link(?=[^>]*rel=\"{Regex.Escape(relation)}\")(?=[^>]*href=\"{Regex.Escape(url)}\")[^>]*>", RegexOptions.CultureInvariant).Count;

    private static int CountAlternate(string source, string language, string url) =>
        Regex.Matches(source, $"<link(?=[^>]*rel=\"alternate\")(?=[^>]*href=\"{Regex.Escape(url)}\")(?=[^>]*hreflang=\"{Regex.Escape(language)}\")[^>]*>", RegexOptions.CultureInvariant).Count;

    private static string ExtractDocumentLinks(string source) => string.Join(
        Environment.NewLine,
        Regex.Matches(source, "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>", RegexOptions.CultureInvariant)
            .Select(match => match.Value));

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
