using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class HomeStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public HomeStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
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
        "Functional Prototypes & Reverse Engineering | MALIEV",
        "Turn ideas and existing parts into CAD and functional prototypes with MALIEV in Nonthaburi, Thailand. 3D design, CNC machining, 3D printing, 3D scanning and suitable molding processes.",
        "functional prototypes, reverse engineering Thailand, 3D design, CNC machining, 3D printing, 3D scanning, replacement parts",
        "Custom parts.",
        "From CAD to assembly.",
        "Our Services",
        "How It Works",
        "Why Choose MALIEV",
        "Have an idea, drawing or sample part?")]
    [InlineData(
        "th",
        "พัฒนาต้นแบบและวิศวกรรมย้อนกลับ | MALIEV",
        "จากไอเดียและชิ้นงานตัวอย่าง สู่แบบ CAD และต้นแบบที่ใช้งานได้ ด้วยงานออกแบบ 3 มิติ CNC, 3D Printing, 3D Scanning และการขึ้นรูปที่เหมาะกับงาน โดย MALIEV นนทบุรี",
        "พัฒนาต้นแบบ, วิศวกรรมย้อนกลับ, ออกแบบ 3 มิติ, CNC, พิมพ์ 3 มิติ, สแกน 3 มิติ, ชิ้นส่วนทดแทน",
        "ชิ้นส่วนสั่งผลิต",
        "จาก CAD ถึงงานประกอบ",
        "บริการของเรา",
        "ขั้นตอนการสั่งงาน",
        "ทำไมต้องเลือก MALIEV",
        "มีไอเดีย แบบงาน หรือชิ้นงานตัวอย่าง?")]
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
        Assert.Contains($"href=\"/InstantQuotation/3D-Printing?culture={culture}\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"href=\"/Services/Index?culture={culture}\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"href=\"/Contact?culture={culture}\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(culture == "th" ? "ดูบริการทั้งหมด" : "View all services", source, StringComparison.Ordinal);
        Assert.Contains("landing-service-card--injection", source, StringComparison.Ordinal);
        Assert.Contains("pp-injection-molded-component.webp", source, StringComparison.Ordinal);
        Assert.Contains("width=\"1700\" height=\"925\" loading=\"lazy\" decoding=\"async\"", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ชิ้นงานฉีดโพลีโพรพิลีนสีน้ำเงิน" : "Blue polypropylene injection-molded mounting component", source, StringComparison.Ordinal);
        Assert.Contains("landing-service-card--directory-link", source, StringComparison.Ordinal);
        Assert.Contains(
            culture == "th"
                ? "ฉีดพลาสติกจำนวนน้อยไม่เกิน 1,000 ชิ้นด้วยเครื่องระบบลม รองรับน้ำหนักฉีดสูงสุด 50 กรัม"
                : "with shot weights up to 50 g",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            culture == "th"
                ? "ผลิตชิ้นงานซิลิโคนจำนวนน้อยด้วยแม่พิมพ์ที่พิมพ์ 3 มิติ โดยตรวจสอบวัสดุและผิวงานให้เหมาะกับการใช้งาน"
                : "with the material and finish reviewed for the application.",
            source,
            StringComparison.Ordinal);
        Assert.Contains("profile-hybrid-gripper-cutout.webp", source, StringComparison.Ordinal);
        Assert.Contains("camera-bracket-deployment.webp", source, StringComparison.Ordinal);
        Assert.Contains("camera-bracket-production-v5.webp", source, StringComparison.Ordinal);
        Assert.Contains("width=\"1200\" height=\"800\" fetchpriority=\"high\" loading=\"eager\"", source, StringComparison.Ordinal);
        Assert.Contains("rel=\"preload\" as=\"image\" href=\"/src/images/landing/profile-hybrid-gripper-cutout.webp\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-website-structured-data\"", source, StringComparison.Ordinal);
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
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/?culture=en"));

        Assert.Contains("<title>Functional Prototypes & Reverse Engineering | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"home-content\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task HomeProcess_PreservesFourLocalizedOrderedStepsWithoutRemovedNumberSpans(string culture, bool active)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Home", active ? "true" : "false"));
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"/?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"home-content\"", source, StringComparison.Ordinal);
        Assert.Equal(active, source.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        var process = Assert.Single(Regex.Matches(source,
            "<section(?=[^>]*id=\"landing-process\")[^>]*>[\\s\\S]*?</section>", RegexOptions.CultureInvariant).Cast<Match>()).Value;
        var ordered = Assert.Single(Regex.Matches(process,
            "<ol(?=[^>]*class=\"landing-process-grid\")[^>]*>[\\s\\S]*?</ol>", RegexOptions.CultureInvariant).Cast<Match>()).Value;
        Assert.DoesNotContain("landing-step-number", process, StringComparison.Ordinal);
        var steps = Regex.Matches(ordered, "<li>[\\s\\S]*?</li>", RegexOptions.CultureInvariant).Cast<Match>().ToArray();
        Assert.Equal(4, steps.Length);
        string[] headings = culture == "th"
            ? ["ไอเดีย แบบงาน หรือชิ้นงานตัวอย่าง", "ออกแบบ 3 มิติ", "เลือกกระบวนการผลิต", "ผลิตและตรวจสอบ"]
            : ["Ideas, drawings or samples", "3D design", "Choose the process", "Build and inspect"];
        string[] descriptions = culture == "th"
            ? ["ส่งสิ่งที่คุณมี พร้อมการใช้งานของชิ้นงาน", "สร้างหรือปรับแบบ CAD จากไอเดียหรือชิ้นงานเดิม", "พิจารณาวัสดุ รูปทรง และจำนวนร่วมกัน", "ผลิตและตรวจสอบตามข้อกำหนดที่ตกลงร่วมกัน"]
            : ["Share what you have and how the part will be used.", "Create or refine CAD from your idea or existing part.", "Review material, geometry and quantity together.", "Build and inspect against the agreed requirements."];
        string[] photographs = ["sketch", "cad", "prototypes", "inspection"];
        Assert.DoesNotContain("landing-step-icon", process, StringComparison.Ordinal);
        Assert.DoesNotContain("camera-bracket", process, StringComparison.Ordinal);
        for (var index = 0; index < steps.Length; index++)
        {
            Assert.Contains($"<h3>{headings[index]}</h3>", steps[index].Value, StringComparison.Ordinal);
            Assert.Contains($"<p>{descriptions[index]}</p>", steps[index].Value, StringComparison.Ordinal);
            Assert.Contains($"home-enclosure-{photographs[index]}.webp", steps[index].Value, StringComparison.Ordinal);
            Assert.Contains("class=\"landing-step-photo\"", steps[index].Value, StringComparison.Ordinal);
            Assert.Contains("width=\"1200\" height=\"800\" loading=\"lazy\" decoding=\"async\"", steps[index].Value, StringComparison.Ordinal);
        }
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
