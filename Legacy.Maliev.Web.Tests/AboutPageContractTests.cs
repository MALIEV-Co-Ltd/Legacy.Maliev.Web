using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

// The 3c052 source redesign supersedes the former chronology-only presentation.
// These checks freeze the replacement's public content and enhancement wire contracts.
public sealed class AboutPageContractTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly TestingWebApplicationFactory factory;

    public AboutPageContractTests(TestingWebApplicationFactory factory) => this.factory = factory;

    [Theory]
    [InlineData("en", "From concept to a working part.", "Manufacturing capabilities", "Selected corporate customers")]
    [InlineData("th", "จากไอเดีย สู่ชิ้นงานจริง", "ความสามารถด้านการผลิต", "ลูกค้าองค์กรบางส่วนของเรา")]
    public async Task About_RendersLocalizedSourceStoryAndAccessibleControls(string culture, string heading, string capabilities, string customers)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/about?culture={culture}"));

        Assert.Single(Regex.Matches(html, "<h1(?:\\s|>)", RegexOptions.IgnoreCase));
        Assert.Contains($">{heading}<", html, StringComparison.Ordinal);
        Assert.Contains(capabilities, html, StringComparison.Ordinal);
        Assert.Contains(customers, html, StringComparison.Ordinal);
        Assert.Contains("class=\"landing-main profile-about\"", html, StringComparison.Ordinal);
        Assert.Contains("data-capability-carousel", html, StringComparison.Ordinal);
        Assert.Contains("data-capability-track", html, StringComparison.Ordinal);
        Assert.Contains("data-capability-status", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("data-workpiece-gallery", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"off\"", html, StringComparison.Ordinal);
        Assert.Contains("data-workpiece-toggle", html, StringComparison.Ordinal);
        foreach (var id in new[] { "cnc", "printing", "scanning", "design", "silicone", "injection" })
        {
            Assert.Single(Regex.Matches(html, $"id=\"capability-{id}\""));
            Assert.Contains($"aria-controls=\"capability-{id}\"", html, StringComparison.Ordinal);
        }
        for (var index = 0; index < 4; index++)
        {
            Assert.Single(Regex.Matches(html, $"id=\"workpiece-photo-{index}\""));
            Assert.Contains($"aria-controls=\"workpiece-photo-{index}\"", html, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("about-records", html, StringComparison.Ordinal);
        Assert.DoesNotContain("first in Thailand", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connect.facebook.net", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task About_PreservesOrderedIllustrativeProjectAndEngineeringLimits()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/about?culture=en"));
        var stages = new[] { "01 · Idea & sketch", "02 · CAD design", "03 · Prototype variations", "04 · CNC batch production" };
        var previous = -1;
        foreach (var stage in stages)
        {
            var position = html.IndexOf(stage, StringComparison.Ordinal);
            Assert.True(position > previous, $"Missing or reordered source stage: {stage}");
            previous = position;
        }
        Assert.Contains("Images illustrate the process and are not a named customer case study.", html, StringComparison.Ordinal);
        Assert.Contains("MJF, SLS, SLM and DMLS require engineering review", html, StringComparison.Ordinal);
        Assert.Contains("up to 1,000 parts", html, StringComparison.Ordinal);
        Assert.Contains("Up to 50 g per shot", html, StringComparison.Ordinal);
        Assert.Contains("up to 350 °C", html, StringComparison.Ordinal);
        Assert.Contains("Editable CAD and 2D drawings are separately defined work.", html, StringComparison.Ordinal);
        Assert.Contains("complete and sign the agreement before sending sensitive drawings", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task About_PreservesExactCustomerRosterAndCorrectedArtwork(string culture)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/about?culture={culture}"));
        string[] expected = [
            "ptt", "pttep", "bangchak", "siemens-mobility", "mitsubishi-materials", "fabrinet",
            "minor-food", "kohler", "grohe", "murata", "olympus", "air-liquide", "lixil", "kobelco",
            "siam-motors", "mektec", "thai-asahi-denso", "yg-1", "sp-syndicate", "singha-kameda",
            "aeon-fantasy", "lenso", "mfec", "thai-coconut", "khon-kaen-sugar", "bem", "crown-seal",
            "thai-optical", "kumwell", "tpi-polene-power",
            "alten-korea", "forth", "do-day-dream", "thai-agri-foods",
            "sahamitr-pressure-container", "index-creative-village"
        ];
        var actual = Regex.Matches(html, "data-customer=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(expected, actual);
        Assert.Contains(culture == "th"
            ? "บริษัทที่เลือกใช้บริการพัฒนาและผลิตชิ้นงานกับ MALIEV"
            : "Companies that have chosen MALIEV for manufacturing and development work.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Companies we have completed manufacturing and development work for.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("บริษัทที่เราเคยร่วมพัฒนาและผลิตชิ้นงานให้", html, StringComparison.Ordinal);
        (string Slug, string English, string Thai, int Width, int Height, bool Dark)[] additions = [
            ("alten-korea", "ALTEN Korea", "ALTEN Korea", 50, 83, false),
            ("forth", "Forth Corporation", "ฟอร์ท คอร์ปอเรชั่น", 169, 50, false),
            ("do-day-dream", "Do Day Dream", "ดู เดย์ ดรีม", 76, 74, false),
            ("thai-agri-foods", "Thai Agri Foods", "ไทยอกริฟู้ดส์", 75, 75, false),
            ("sahamitr-pressure-container", "Sahamitr Pressure Container", "สหมิตรถังแก๊ส", 264, 168, true),
            ("index-creative-village", "Index Creative Village", "อินเด็กซ์ ครีเอทีฟ วิลเลจ", 192, 100, true)
        ];
        foreach (var customer in additions)
        {
            var card = Assert.Single(Regex.Matches(html,
                $"<li class=\"profile-customer\" data-customer=\"{customer.Slug}\">(?<card>.*?)</li>", RegexOptions.Singleline)).Groups["card"].Value;
            Assert.Contains($"class=\"profile-customer-logo{(customer.Dark ? " profile-customer-logo-dark" : "")}\"", card, StringComparison.Ordinal);
            Assert.Contains($"src=\"/src/images/customers/{customer.Slug}.webp\" width=\"{customer.Width}\" height=\"{customer.Height}\" loading=\"lazy\" decoding=\"async\" alt=\"\"", card, StringComparison.Ordinal);
            Assert.Contains($">{(culture == "th" ? customer.Thai : customer.English)}<", card, StringComparison.Ordinal);
        }
        Assert.Contains("/src/images/customers/kobelco.webp", html, StringComparison.Ordinal);
        Assert.Contains("/src/images/customers/yg-1-official-mark.webp", html, StringComparison.Ordinal);
        Assert.Contains("/src/images/customers/tpi-polene-power-emblem.webp", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/src/images/customers/yg-1.png", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/src/images/customers/tpi-polene-power.png", html, StringComparison.Ordinal);
    }
}
