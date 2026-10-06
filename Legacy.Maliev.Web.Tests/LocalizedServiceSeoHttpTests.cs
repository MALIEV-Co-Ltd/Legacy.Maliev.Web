using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Couples visible localized service navigation and workshop details to actual Program JSON-LD.</summary>
public sealed class LocalizedServiceSeoHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/services/3d-printing", "en")]
    [InlineData("/services/3d-printing", "th")]
    [InlineData("/services/3d-scanning", "en")]
    [InlineData("/services/3d-scanning", "th")]
    [InlineData("/services/cnc-machining", "en")]
    [InlineData("/services/cnc-machining", "th")]
    [InlineData("/services/custom-manufacturing", "en")]
    [InlineData("/services/custom-manufacturing", "th")]
    public async Task LocalizedService_VisibleBreadcrumbLocationAndGuidesMatchOwnedDocumentContract(
        string route, string culture)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        var html = await GetDocument(client, route + "?culture=" + culture, culture);
        var schemas = Schemas(html);
        AssertBreadcrumb(html, Assert.Single(schemas, schema => Type(schema) == "BreadcrumbList"), route, culture);
        AssertLocation(html, Assert.Single(schemas, schema => Type(schema) == "LocalBusiness"), culture);
        await AssertGuideLinks(client, html, route, culture);
    }

    private static void AssertBreadcrumb(string html, JsonElement schema, string route, string culture)
    {
        var names = new[]
        {
            culture == "th" ? "หน้าแรก" : "Home",
            culture == "th" ? "บริการ" : "Services",
            (route, culture) switch
            {
                ("/services/3d-printing", "th") => "พิมพ์ 3D",
                ("/services/3d-scanning", "th") => "สแกน 3D",
                ("/services/cnc-machining", "th") => "งาน CNC",
                ("/services/custom-manufacturing", "th") => "ผลิตชิ้นงานตามแบบ",
                ("/services/3d-printing", _) => "3D printing",
                ("/services/3d-scanning", _) => "3D scanning",
                ("/services/cnc-machining", _) => "CNC machining",
                _ => "Custom manufacturing",
            },
        };
        var paths = new[] { "/", "/services", route };
        var items = schema.GetProperty("itemListElement").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        var navigation = Assert.Single(Elements(RemoveScripts(html), "nav"), element =>
            Attribute(element.Attributes, "data-migration-component") == "service-breadcrumb");
        Assert.Equal(culture == "th" ? "เส้นทางนำทาง" : "Breadcrumb", Attribute(navigation.Attributes, "aria-label"));
        var visibleItems = Elements(navigation.Body, "li");
        Assert.Equal(3, visibleItems.Length);
        for (var index = 0; index < items.Length; index++)
        {
            Assert.Equal("ListItem", Type(items[index]));
            Assert.Equal(index + 1, items[index].GetProperty("position").GetInt32());
            Assert.Equal(names[index], items[index].GetProperty("name").GetString());
            Assert.Equal(LocalizedUrl(paths[index], culture), items[index].GetProperty("item").GetString());
            Assert.Equal(names[index], Text(visibleItems[index].Body));
            if (index < 2)
            {
                Assert.Empty(Attribute(visibleItems[index].Attributes, "aria-current"));
                var link = Assert.Single(Elements(visibleItems[index].Body, "a"));
                Assert.Equal(LocalizedUrl(paths[index], culture), Attribute(link.Attributes, "href"));
            }
            else
            {
                Assert.Equal("page", Attribute(visibleItems[index].Attributes, "aria-current"));
                Assert.Empty(Elements(visibleItems[index].Body, "a"));
            }
        }
    }

    private static void AssertLocation(string html, JsonElement schema, string culture)
    {
        Assert.Equal("https://www.maliev.com/#organization", schema.GetProperty("@id").GetString());
        Assert.Equal("Maliev Co., Ltd.", schema.GetProperty("name").GetString());
        Assert.Equal("+66898950690", schema.GetProperty("telephone").GetString());
        var address = schema.GetProperty("address");
        Assert.Equal("PostalAddress", Type(address));
        Assert.Equal("36/1 Moo 3, Khlong Khoi", address.GetProperty("streetAddress").GetString());
        Assert.Equal("Pak Kret", address.GetProperty("addressLocality").GetString());
        Assert.Equal("Nonthaburi", address.GetProperty("addressRegion").GetString());
        Assert.Equal("11120", address.GetProperty("postalCode").GetString());
        Assert.Equal("TH", address.GetProperty("addressCountry").GetString());
        Assert.Equal(13.9469417, schema.GetProperty("geo").GetProperty("latitude").GetDouble(), 7);
        Assert.Equal(100.4588118, schema.GetProperty("geo").GetProperty("longitude").GetDouble(), 7);
        var hours = Assert.Single(schema.GetProperty("openingHoursSpecification").EnumerateArray());
        Assert.Equal(new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
            hours.GetProperty("dayOfWeek").EnumerateArray().Select(day => day.GetString()).ToArray());
        Assert.Equal("09:00", hours.GetProperty("opens").GetString());
        Assert.Equal("18:00", hours.GetProperty("closes").GetString());

        var location = Assert.Single(Elements(RemoveScripts(html), "section"), element =>
            Attribute(element.Attributes, "data-migration-component") == "service-location");
        var visibleAddress = Text(Assert.Single(Elements(location.Body, "address")).Body);
        var expectedAddress = culture == "th"
            ? "Maliev Co., Ltd. 36/1 หมู่ 3 ตำบลคลองข่อย อำเภอปากเกร็ด จังหวัดนนทบุรี 11120 ประเทศไทย"
            : "Maliev Co., Ltd. 36/1 Moo 3, Khlong Khoi, Pak Kret, Nonthaburi 11120, Thailand";
        Assert.Equal(WithoutWhitespace(expectedAddress), WithoutWhitespace(visibleAddress));
        var visible = Text(location.Body);
        Assert.Contains(culture == "th" ? "จันทร์-ศุกร์: 09:00-18:00 น." : "Monday-Friday: 09:00-18:00", visible, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "เสาร์-อาทิตย์: ปิด" : "Saturday-Sunday: Closed", visible, StringComparison.Ordinal);
        var links = Elements(location.Body, "a");
        AssertLink(links, "https://goo.gl/maps/AXqLnqUu5dM2", culture == "th" ? "เปิด Google Maps" : "Open Google Maps");
        Assert.Equal("https://goo.gl/maps/AXqLnqUu5dM2", schema.GetProperty("hasMap").GetString());
        AssertLink(links, "https://line.me/ti/p/@maliev", culture == "th" ? "ติดต่อผ่าน LINE OA" : "Chat on LINE OA");
        AssertLink(links, "/quotation", culture == "th" ? "ส่งไฟล์เพื่อประเมิน" : "Send files for review");
    }

    private static async Task AssertGuideLinks(HttpClient client, string html, string route, string culture)
    {
        var links = Elements(RemoveScripts(html), "a");
        if (route == "/services/custom-manufacturing")
        {
            var processChoices = Assert.Single(Elements(RemoveScripts(html), "section"), element =>
                Attribute(element.Attributes, "aria-labelledby") == "choose-process-title");
            links = Elements(processChoices.Body, "a");
            var handoffs = new[]
            {
                ("/Services/CNC-Machining", "Review CNC machining", "ดูบริการ CNC"),
                ("/Services/3D-Printing", "Review 3D printing", "รับพิมพ์ 3D"),
                ("/Services/3D-Scanning", "Review 3D scanning", "รับสแกน 3D"),
            };
            foreach (var (path, english, thai) in handoffs)
            {
                AssertLink(links, path, culture == "th" ? thai : english);
                using var redirect = await client.GetAsync(path + "?culture=" + culture);
                Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
                var target = path.ToLowerInvariant() + "?culture=" + culture;
                Assert.Equal(target, redirect.Headers.Location?.OriginalString);
                var owned = await GetDocument(client, target, culture);
                Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", owned, StringComparison.Ordinal);
            }
            return;
        }

        // These source-owned guides are on Shopify. Freeze their rendered URL and
        // localized label without contacting the external host or claiming its readiness.
        var guides = route switch
        {
            "/services/3d-printing" => new[]
            {
                ("fdm-part-orientation-strength-load-path", "Read the FDM orientation guide", "อ่านคู่มือทิศทางพิมพ์ FDM"),
                ("3d-print-clearance-interference-mating-parts", "Read the clearance guide", "อ่านคู่มือระยะเผื่อ"),
                ("fdm-vs-dlp", "Compare FDM and DLP", "เปรียบเทียบ FDM และ DLP"),
            },
            "/services/3d-scanning" => new[]
            {
                ("prepare-part-for-3d-scanning-service", "Read the scanning-preparation guide", "อ่านคู่มือเตรียมชิ้นงานสแกน"),
                ("3d-scan-to-cad-reverse-engineering", "Read the scan-to-CAD guide", "อ่านคู่มือจากสแกนสู่ CAD"),
                ("recreate-replacement-part-without-cad", "Read the replacement-part guide", "อ่านคู่มือสร้างอะไหล่ทดแทน"),
            },
            _ => new[]
            {
                ("specify-threaded-holes-cnc-machining", "Read the threaded-hole guide", "อ่านคู่มือการระบุรูเกลียว"),
                ("cnc-internal-corners-radius-mating-parts", "Read the internal-corner guide", "อ่านคู่มือมุมในงาน CNC"),
                ("cnc-thin-wall-design-workholding", "Read the thin-wall guide", "อ่านคู่มือผนังบาง"),
            },
        };
        foreach (var (slug, english, thai) in guides)
            AssertLink(links, "https://shop.maliev.com/blogs/news/" + slug, culture == "th" ? thai : english);
    }

    private static async Task<string> GetDocument(HttpClient client, string path, string culture)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        var document = Assert.Single(Regex.Matches(html, "<html\\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Equal(culture, Attribute(document.Groups["attributes"].Value, "lang"));
        return html;
    }

    private static void AssertLink(Element[] links, string href, string label)
    {
        var link = Assert.Single(links, element => Attribute(element.Attributes, "href") == href);
        Assert.Equal(label, Text(link.Body));
    }

    private static JsonElement[] Schemas(string html) => Elements(html, "script")
        .Where(element => Attribute(element.Attributes, "type") == "application/ld+json")
        .Select(element =>
        {
            using var document = JsonDocument.Parse(element.Body);
            Assert.Equal("https://schema.org", document.RootElement.GetProperty("@context").GetString());
            return document.RootElement.Clone();
        }).ToArray();

    private static Element[] Elements(string html, string tag) => Regex.Matches(html,
            $"<{tag}\\b(?<attributes>[^>]*)>(?<body>.*?)</{tag}\\s*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        .Cast<Match>().Select(match => new Element(match.Groups["attributes"].Value, match.Groups["body"].Value)).ToArray();

    private static string Attribute(string attributes, string name) => WebUtility.HtmlDecode(Regex.Match(attributes,
        $"(?:^|\\s){Regex.Escape(name)}\\s*=\\s*[\"'](?<value>[^\"']*)[\"']", RegexOptions.IgnoreCase).Groups["value"].Value);

    private static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " ")), "\\s+", " ").Trim();
    private static string WithoutWhitespace(string text) => Regex.Replace(text, "\\s+", "");
    private static string RemoveScripts(string html) => Regex.Replace(html, "<script\\b[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static string Type(JsonElement element) => element.GetProperty("@type").GetString()!;
    private static string LocalizedUrl(string path, string culture) => "https://www.maliev.com" + path + (culture == "en" ? "?culture=en" : string.Empty);
    private sealed record Element(string Attributes, string Body);
}
