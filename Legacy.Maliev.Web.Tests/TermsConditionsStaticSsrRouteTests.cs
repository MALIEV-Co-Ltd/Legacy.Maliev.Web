using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class TermsConditionsStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TermsConditionsStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheTermsConditionsRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Legal", "TermsConditionsPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Legal", "TermsConditionsContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Legal", "TermsConditions.cshtml"));

        Assert.Contains("BlazorRouting:TermsConditions", program, StringComparison.Ordinal);
        Assert.Contains("\"/Legal/TermsConditions\"", program, StringComparison.Ordinal);
        Assert.Contains("\"TermsConditions\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(TermsConditionsContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Terms and Conditions | MALIEV",
        "These terms and conditions outline the rules and regulations for the use of Maliev Co., Ltd.'s Website.",
        "cookies, license, hyperlink, terms and conditions, terms, services, iframes, rights, liability, disclaimer",
        "These terms and conditions outline the rules and regulations",
        "we will not be liable for any loss or damage of any nature")]
    [InlineData(
        "th",
        "ข้อกำหนดและเงื่อนไขการให้บริการ | MALIEV",
        "ข้อกำหนดและเงื่อนไขการเก็บข้อมูลและใช้บริการต่างๆของเรา",
        "นโยบายความเป็นส่วนตัว, cookies, license, hyperlink, terms, services, iframes, rights, liability, disclaimer",
        "ข้อกำหนดและเงื่อนไข",
        "เราจะไม่รับผิดต่อความสูญเสียหรือความเสียหายไม่ว่าประเภทใด")]
    public async Task TermsConditionsRoute_RendersLocalizedStaticSsrWithSeoAnalyticsAndDocumentParity(
        string culture,
        string title,
        string description,
        string keywords,
        string firstBodyMarker,
        string finalBodyMarker)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/legal/termsconditions?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"keywords\" content=\"{keywords}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains(firstBodyMarker, source, StringComparison.Ordinal);
        Assert.Contains(finalBodyMarker, source, StringComparison.Ordinal);
        Assert.Contains("<nav class=\"legal-toc\" aria-label=", source, StringComparison.Ordinal);
        Assert.Contains("<article class=\"legal-document\">", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
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

    [Fact]
    public async Task LegacyMixedCaseTermsConditionsUrl_PreservesTheCanonicalRedirectStatus()
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/Legal/TermsConditions?culture=en");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/legal/termsconditions?culture=en", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheTermsConditionsRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/legal/termsconditions?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/legal/termsconditions?culture=en");
        request.Headers.Add("Cookie", consentCookie.Split(';', 2)[0]);
        using var response = await client.SendAsync(request);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'granted';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("https://www.googletagmanager.com/ns.html?id=GTM-KHDDLVRR", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/legal/termsconditions?culture=en", "https://www.maliev.com/legal/termsconditions?culture=en", "https://www.maliev.com/legal/termsconditions")]
    [InlineData("th", "https://www.maliev.com/legal/termsconditions", "https://www.maliev.com/legal/termsconditions?culture=en", "https://www.maliev.com/legal/termsconditions")]
    public async Task TermsConditionsRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/legal/termsconditions?culture={culture}&tracking=excluded"));

        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledTermsConditionsRoute_UsesTheRetainedRazorFallback()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:TermsConditions", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/legal/termsconditions?culture=en"));

        Assert.Contains("<title>Terms and Conditions | MALIEV</title>", source, StringComparison.Ordinal);
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

    [GeneratedRegex("data-cookie-string=\"(?<cookie>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ConsentCookieRegex();
}
