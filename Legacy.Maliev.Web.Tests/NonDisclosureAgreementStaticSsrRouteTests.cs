using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class NonDisclosureAgreementStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public NonDisclosureAgreementStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
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
        Assert.Contains(culture == "th"
            ? "ปกป้องข้อมูลงานผลิตที่เป็นความลับก่อนส่งไฟล์โครงการ"
            : "Protect confidential manufacturing information before sharing project files.", source, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task NonDisclosureAgreementRoute_PreservesSourceExplanationsAndAnchors(
        bool useBlazorRoute, string culture)
    {
        await using var routeFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:NonDisclosureAgreement", useBlazorRoute ? "true" : "false"));
        using var client = CreateClient(routeFactory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync($"/legal/nondisclosureagreement?culture={culture}", deadline.Token);
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(deadline.Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Equal(useBlazorRoute, source.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        var article = Regex.Match(source, "<article\\b[^>]*class=\"legal-document\"[^>]*>(?<content>.*?)</article>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(article.Success);
        var content = article.Groups["content"].Value;
        var sections = new[]
        {
            (Id: "what-it-covers",
                Heading: culture == "th"
                    ? "สัญญานี้ครอบคลุมแบบ แบบจำลอง CAD ตัวอย่างชิ้นงาน ข้อกำหนด การพูดคุยเรื่องราคา และข้อมูลโครงการที่ส่งเพื่อขอราคา หรือประเมินงานผลิต"
                    : "What the agreement covers",
                Copy: culture == "th"
                    ? "สัญญานี้ใช้กับแบบ แบบจำลอง CAD ตัวอย่างชิ้นงาน ข้อกำหนด การพูดคุยเรื่องราคา และข้อมูลโครงการที่เป็นความลับซึ่งส่งเพื่อขอราคาหรือประเมินงานผลิต"
                    : "The agreement is intended for confidential drawings, CAD files, physical samples, specifications, pricing discussions, and other project information shared for quotation or manufacturing review."),
            (Id: "when-to-send",
                Heading: culture == "th"
                    ? "หากโครงการต้องใช้ NDA ให้กรอกและลงนามก่อนส่งไฟล์หรือตัวอย่างที่เป็นความลับ พร้อมแจ้งชื่อโครงการให้ทีมงานทราบ"
                    : "When to send it",
                Copy: culture == "th"
                    ? "หากโครงการต้องใช้ NDA ให้กรอกและลงนามในแบบฟอร์มก่อนส่งไฟล์หรือตัวอย่างที่เป็นความลับ และแจ้งชื่อโครงการให้ทีมงานทราบเพื่อดำเนินการได้ถูกต้อง"
                    : "If your project requires an NDA, complete and sign the template before sending sensitive files or samples. Tell our team which project the agreement belongs to so we can route it correctly."),
        };
        foreach (var section in sections)
        {
            var pattern = $"<h3\\b[^>]*\\bid=\"{section.Id}\"[^>]*>{Regex.Escape(section.Heading)}</h3>\\s*<p>{Regex.Escape(section.Copy)}</p>";
            Assert.Single(Regex.Matches(content, pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant).Cast<Match>());
        }

        Assert.Contains(culture == "th"
            ? "ที่ MALIEV เราให้ความสำคัญกับการรักษาความลับของงานและข้อมูลต่าง ๆ ของลูกค้าเป็นอย่างมาก"
            : "At Maliev, we take our customer's confidentiality very serious.", content, StringComparison.Ordinal);
        Assert.True(content.IndexOf("id=\"what-it-covers\"", StringComparison.Ordinal)
            < content.IndexOf("id=\"when-to-send\"", StringComparison.Ordinal));
        Assert.True(content.IndexOf("id=\"when-to-send\"", StringComparison.Ordinal)
            < content.IndexOf("id=\"download\"", StringComparison.Ordinal));
        var download = Regex.Match(content, "<a\\b[^>]*\\bid=\"download\"[^>]*>", RegexOptions.CultureInvariant);
        Assert.True(download.Success);
        Assert.Contains("href=\"https://storage.googleapis.com/maliev.com/web-contents/documents/mutual%20non-disclosure%20agreement.pdf\"", download.Value, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", download.Value, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener\"", download.Value, StringComparison.Ordinal);
        Assert.Matches("<a\\b[^>]*href=\"/contact#contact-us\"[^>]*data-contact-email=\"nda@maliev.com\"[^>]*>nda@maliev.com</a>", content);
        Assert.DoesNotContain("href=\"mailto:", content, StringComparison.OrdinalIgnoreCase);
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
