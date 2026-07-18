using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class HomeStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public HomeStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheHomePageAntiforgeryLanguageEndpointAndRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Home", "HomePage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Home", "HomeContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Index.cshtml"));

        Assert.Contains("BlazorRouting:Home", program, StringComparison.Ordinal);
        Assert.Contains("\"/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("app.MapPost", program, StringComparison.Ordinal);
        Assert.Contains("RequireAntiforgeryTokenAttribute(true)", program, StringComparison.Ordinal);
        Assert.Contains("CookieRequestCultureProvider.DefaultCookieName", program, StringComparison.Ordinal);
        Assert.Contains("Results.LocalRedirect", program, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(HomeContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Manufacturing Services Bangkok Nonthaburi: CNC Machining, 3D Printing & 3D Scanning",
        "Professional CNC machining, 3D printing, and 3D scanning services in Bangkok and Nonthaburi, Thailand. Upload your file for an instant quote.",
        "3d printing thailand, 3d scanning thailand, cnc machining thailand, manufacturing services bangkok, cnc machining nonthaburi",
        "Precision. Speed. Reliability.",
        "Manufacturing Services for Prototypes and Production Parts",
        "Our Services",
        "How It Works",
        "Why Choose MALIEV",
        "Ready to bring your ideas to life?")]
    [InlineData(
        "th",
        "บริการผลิตชิ้นส่วนออนไลน์ นนทบุรี กรุงเทพ: CNC, พิมพ์ 3 มิติ, สแกน 3 มิติ",
        "บริการพิมพ์ 3 มิติ ชิ้นงาน CNC และสแกน 3 มิติ ในนนทบุรีและกรุงเทพ อัปโหลดไฟล์เพื่อประเมินราคาทันที ไม่มีจำนวนขั้นต่ำ",
        "รับปริ้น 3D, รับพิมพ์ 3 มิติ, รับสแกน 3D, รับ CNC ตามแบบ, โรงกลึง นนทบุรี, CNC กรุงเทพ",
        "แม่นยำ รวดเร็ว เชื่อถือได้",
        "บริการผลิตชิ้นงานต้นแบบและชิ้นส่วนสำหรับการผลิตจริง",
        "บริการของเรา",
        "ขั้นตอนการสั่งงาน",
        "ทำไมต้องเลือก MALIEV",
        "พร้อมเปลี่ยนไอเดียของคุณให้เป็นชิ้นงานจริงหรือยัง?")]
    public async Task HomeRoute_RendersCompleteLocalizedSeoAndLandingContent(
        string culture,
        string title,
        string description,
        string keywords,
        string eyebrow,
        string heading,
        string services,
        string process,
        string reasons,
        string finalCallToAction)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"keywords\" content=\"{keywords}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{eyebrow}<", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains($">{services}<", source, StringComparison.Ordinal);
        Assert.Contains($">{process}<", source, StringComparison.Ordinal);
        Assert.Contains($">{reasons}<", source, StringComparison.Ordinal);
        Assert.Contains($">{finalCallToAction}<", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/InstantQuotation/3D-Printing\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/Contact\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("landing-hero-cnc.webp", source, StringComparison.Ordinal);
        Assert.Contains("landing-hero-printing.webp", source, StringComparison.Ordinal);
        Assert.Contains("landing-hero-scanning.webp", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new WOW", source, StringComparison.Ordinal);
        Assert.DoesNotContain("wowjs", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"wow", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/?culture=en", "https://www.maliev.com/?culture=en", "https://www.maliev.com/")]
    [InlineData("th", "https://www.maliev.com/", "https://www.maliev.com/?culture=en", "https://www.maliev.com/")]
    public async Task HomeRoute_PreservesCanonicalAndLocalizedAlternates(string culture, string canonical, string english, string thai)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/?culture={culture}&tracking=excluded"));

        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheHomeRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/?culture=en");
        request.Headers.Add("Cookie", consentCookie.Split(';', 2)[0]);
        using var response = await client.SendAsync(request);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("var consentState = 'granted';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("https://www.googletagmanager.com/ns.html?id=GTM-KHDDLVRR", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LanguageSelector_RequiresAntiforgeryAndPreservesTheLocalReturnUrl()
    {
        using var client = CreateClient(factory);
        using var rejected = await client.PostAsync(
            "/?handler=SetLanguage&returnUrl=~/",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["culture"] = "en" }));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/?culture=en"));
        var form = LanguageFormRegex().Match(source);
        var token = AntiforgeryTokenRegex().Match(form.Value);
        Assert.True(form.Success);
        Assert.True(token.Success);

        using var accepted = await client.PostAsync(
            form.Groups["action"].Value,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"] = "th",
                ["__RequestVerificationToken"] = token.Groups["value"].Value
            }));

        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal("/", accepted.Headers.Location?.OriginalString);
        Assert.Contains(
            accepted.Headers.GetValues("Set-Cookie"),
            value => value.Contains(".AspNetCore.Culture=c%3Dth%7Cuic%3Dth", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisabledHomeRoute_UsesTheRetainedRazorFallback()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Home", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = await client.GetStringAsync("/?culture=en");

        Assert.Contains("<title>Manufacturing Services Bangkok Nonthaburi: CNC Machining, 3D Printing &amp; 3D Scanning</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"home-content\"", source, StringComparison.Ordinal);
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

    [GeneratedRegex("data-cookie-string=\"(?<cookie>[^\"]+)\"")]
    private static partial Regex ConsentCookieRegex();

    [GeneratedRegex("<form[^>]*class=\"maliev-language-form\"[^>]*action=\"(?<action>[^\"]+)\"[\\s\\S]*?</form>", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageFormRegex();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenRegex();
}
