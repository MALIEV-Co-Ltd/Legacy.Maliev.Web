using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SocialMediaStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly string[] SocialDestinations =
    [
        "https://www.youtube.com/channel/UCCosquPSUed6UPlMcRCq0Ig",
        "https://www.tiktok.com/@maliev.marketing",
        "https://www.instagram.com/maliev.manufacturing/",
        "https://www.threads.com/@maliev.manufacturing",
        "https://www.facebook.com/maliev.manufacturing/",
        "https://line.me/ti/p/@maliev",
        "https://wa.me/66898950690",
        "https://m.me/maliev.manufacturing/",
    ];

    private readonly WebApplicationFactory<Program> factory;

    public SocialMediaStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresTheSocialMediaRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "About", "SocialMediaPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "About", "SocialMediaContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "About", "SocialMedia.cshtml"));

        Assert.Contains("BlazorRouting:SocialMedia", program, StringComparison.Ordinal);
        Assert.Contains("\"/About/SocialMedia\"", program, StringComparison.Ordinal);
        Assert.Contains("\"SocialMedia\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(SocialMediaContent)\"", fallback, StringComparison.Ordinal);

        foreach (var layoutPath in new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml"),
            Path.Combine(web, "Areas", "Member", "Pages", "Shared", "_LayoutMember.cshtml"),
            Path.Combine(web, "Areas", "Knowledges", "Pages", "Shared", "_LayoutKnowledges.cshtml")
        })
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.DoesNotContain("_AccouncementPartial", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_LineOaRecoveryAlertPartial", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("ServiceNotices", layout, StringComparison.Ordinal);
        }
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_AccouncementPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_LineOaRecoveryAlertPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "tests", "VerifyLineOaNavToggle.ps1")));
        Assert.False(File.Exists(Path.Combine(web, "tests", "VerifyLineOaRecoveryBanner.ps1")));
    }

    [Theory]
    [InlineData(
        "en",
        "Social media | MALIEV",
        "Follow MALIEV for manufacturing examples, process updates, and company news.",
        "MALIEV on social media")]
    [InlineData(
        "th",
        "โซเชียลมีเดีย | MALIEV",
        "ติดตามตัวอย่างงานผลิต ข่าวกระบวนการ และข่าวสารจาก MALIEV",
        "MALIEV บนโซเชียลมีเดีย")]
    public async Task SocialMediaRoute_RendersLocalizedStaticSsrWithSeoAnalyticsAndLinkParity(
        string culture,
        string title,
        string description,
        string heading)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/about/socialmedia?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "ชมตัวอย่างงานผลิต โครงการสแกน 3 มิติ ชิ้นงานพิมพ์ และข้อมูลการผลิตที่นำไปใช้ได้จริง"
            : "See production examples, 3D scanning projects, printed parts, and practical manufacturing updates.", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "วิดีโอกระบวนการและตัวอย่างโครงการ" : "Process videos and project demonstrations", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Equal(SocialDestinations.Length, Regex.Matches(source, "class=\"maliev-panel social-link-card\"", RegexOptions.CultureInvariant).Count);
        foreach (var destination in SocialDestinations)
        {
            Assert.Contains($"href=\"{destination}\" target=\"_blank\" rel=\"noopener\"", source, StringComparison.Ordinal);
        }

        var lineCard = Assert.Single(Regex.Matches(
            source,
            """<a class="maliev-panel social-link-card" href="https://line\.me/ti/p/@maliev"[^>]*>[\s\S]*?</a>""",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains("<strong>LINE</strong>", lineCard.Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "<span>แชตกับทีมงานผ่าน LINE Official Account</span>"
            : "<span>Chat with our team through LINE Official Account</span>", lineCard.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("maliev-service-notice", source, StringComparison.Ordinal);
        Assert.DoesNotContain("maliev-service-notice-dismissed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("check the site notice", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ช่องทางข้อความ โปรดตรวจสอบประกาศบนเว็บไซต์สำหรับสถานะล่าสุด", source, StringComparison.Ordinal);
        foreach (var (destination, name, english, thai, placement) in new[]
        {
            ("https://www.facebook.com/maliev.manufacturing/", "Facebook", "Company news and customer-facing updates", "ข่าวบริษัทและประกาศสำหรับลูกค้า", string.Empty),
            ("https://wa.me/66898950690", "WhatsApp", "Message our team through WhatsApp", "ส่งข้อความถึงทีมงานผ่าน WhatsApp", "about_whatsapp"),
            ("https://m.me/maliev.manufacturing/", "Messenger", "Message our team through Facebook Messenger", "ส่งข้อความถึงทีมงานผ่าน Facebook Messenger", "about_messenger")
        })
        {
            var card = Assert.Single(Regex.Matches(
                source,
                $"<a class=\"maliev-panel social-link-card\" href=\"{Regex.Escape(destination)}\"[^>]*>[\\s\\S]*?</a>",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.Contains($"<strong>{name}</strong>", card.Value, StringComparison.Ordinal);
            Assert.Contains($"<span>{(culture == "th" ? thai : english)}</span>", card.Value, StringComparison.Ordinal);
            if (placement.Length > 0)
            {
                Assert.Contains($"data-contact-placement=\"{placement}\"", card.Value, StringComparison.Ordinal);
            }
            if (name == "WhatsApp")
            {
                Assert.Contains("data-maliev-contact-destination=\"whatsapp_business\"", card.Value, StringComparison.Ordinal);
            }
        }

        // Source 3879b78 resolves the temporary LINE incident, not the contact channels.
        Assert.DoesNotContain("maliev-service-notice", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Our LINE Official Account has been compromised", source, StringComparison.Ordinal);
        Assert.DoesNotContain("บัญชี LINE Official Account ของเราถูกเข้าถึงโดยไม่ได้รับอนุญาต", source, StringComparison.Ordinal);
        Assert.DoesNotContain("check the site notice", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("โปรดตรวจสอบประกาศบนเว็บไซต์", source, StringComparison.Ordinal);
        var lineAriaLabel = culture == "th" ? "ติดต่อ MALIEV ผ่าน LINE Official Account" : "Contact MALIEV through LINE Official Account";
        var messengerAriaLabel = culture == "th" ? "ติดต่อ MALIEV ผ่าน Messenger" : "Contact MALIEV through Messenger";
        var whatsappAriaLabel = culture == "th" ? "ติดต่อ MALIEV ผ่าน WhatsApp" : "Contact MALIEV through WhatsApp";
        var lineDescription = culture == "th"
            ? "แชตกับทีมงานผ่าน LINE Official Account"
            : "Chat with our team through LINE Official Account";
        Assert.Contains($">{lineDescription}<", source, StringComparison.Ordinal);
        Assert.Contains($"href=\"https://line.me/ti/p/@maliev\" target=\"_blank\" rel=\"noopener\" aria-label=\"{lineAriaLabel}\"", source, StringComparison.Ordinal);
        Assert.Contains($"href=\"https://m.me/maliev.manufacturing/\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"{messengerAriaLabel}\"", source, StringComparison.Ordinal);
        Assert.Contains($"href=\"https://wa.me/66898950690\" target=\"_blank\" rel=\"noopener\" aria-label=\"{whatsappAriaLabel}\"", source, StringComparison.Ordinal);

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
    [InlineData("en", "https://www.maliev.com/about/socialmedia?culture=en", "https://www.maliev.com/about/socialmedia?culture=en", "https://www.maliev.com/about/socialmedia")]
    [InlineData("th", "https://www.maliev.com/about/socialmedia", "https://www.maliev.com/about/socialmedia?culture=en", "https://www.maliev.com/about/socialmedia")]
    public async Task SocialMediaRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/about/socialmedia?culture={culture}&tracking=excluded"));

        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledSocialMediaRoute_UsesTheRetainedRazorFallback()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:SocialMedia", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/about/socialmedia?culture=en"));

        Assert.Contains("<title>Social media | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("href=\"https://www.facebook.com/maliev.manufacturing/\" target=\"_blank\" rel=\"noopener\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both route owners retain the verified social cards, decorative icons, and localized descriptions.
    /// </summary>
    /// <param name="culture">The requested public-page culture.</param>
    /// <param name="active">Whether the static SSR route owns the request.</param>
    /// <param name="tikTokDescription">The expected localized short-video description.</param>
    /// <param name="threadsDescription">The expected localized conversation description.</param>
    /// <returns>A task that completes after the real HTTP response is inspected.</returns>
    [Theory]
    [InlineData("en", true, "Short manufacturing videos and process updates", "Manufacturing conversations and company updates")]
    [InlineData("th", true, "วิดีโอการผลิตแบบสั้นและข่าวกระบวนการ", "บทสนทนาด้านการผลิตและข่าวสารบริษัท")]
    [InlineData("en", false, "Short manufacturing videos and process updates", "Manufacturing conversations and company updates")]
    [InlineData("th", false, "วิดีโอการผลิตแบบสั้นและข่าวกระบวนการ", "บทสนทนาด้านการผลิตและข่าวสารบริษัท")]
    public async Task SocialProfileCards_PreserveVerifiedIconsAndLocalizedDescriptions(
        string culture,
        bool active,
        string tikTokDescription,
        string threadsDescription)
    {
        using var routeFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:SocialMedia", active.ToString()));
        using var client = CreateClient(routeFactory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync($"/about/socialmedia?culture={culture}", timeout.Token);
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(timeout.Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Equal(active, source.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        foreach (var (destination, icon, label, description) in new[]
        {
            ("https://www.tiktok.com/@maliev.marketing", "fa-tiktok", "TikTok", tikTokDescription),
            ("https://www.threads.com/@maliev.manufacturing", "fa-threads", "Threads", threadsDescription),
        })
        {
            var card = Assert.Single(
                Regex.Matches(source, "<a\\b[^>]*class=\"maliev-panel social-link-card\"[^>]*>.*?</a>", RegexOptions.CultureInvariant | RegexOptions.Singleline)
                    .Cast<Match>(),
                match => match.Value.Contains($"href=\"{destination}\"", StringComparison.Ordinal)).Value;

            Assert.Contains("target=\"_blank\" rel=\"noopener\"", card, StringComparison.Ordinal);
            Assert.Contains($"<i class=\"fab {icon}\" aria-hidden=\"true\"></i>", card, StringComparison.Ordinal);
            Assert.Contains($"<strong>{label}</strong>", card, StringComparison.Ordinal);
            Assert.Contains($"<span>{description}</span>", card, StringComparison.Ordinal);
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
}
