using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SitemapResponseCachingHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly string[] IndexedPaths =
    [
        "/", "/services", "/services/custom-manufacturing", "/services/3d-design",
        "/services/silicone-casting", "/services/low-volume-injection-molding",
        "/services/cnc-machining", "/services/3d-printing", "/services/3d-scanning",
        "/services/finishing-and-color", "/about", "/about/socialmedia", "/contact", "/career",
        "/instantquotation/3d-printing", "/knowledges", "/knowledges/guidelines",
        "/knowledges/workflow", "/knowledges/specifications",
        "/knowledges/specifications/cnc-machining", "/knowledges/specifications/3d-printing",
        "/knowledges/specifications/3d-scanning", "/legal", "/legal/privacypolicy",
        "/legal/termsconditions", "/legal/nondisclosureagreement", "/no-weapons"
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Public_sitemap_declares_source_one_hour_public_cache_lifetime(string culture)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"/sitemap?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var cache = response.Headers.CacheControl;
        Assert.NotNull(cache);
        Assert.True(cache.Public);
        Assert.Equal(TimeSpan.FromSeconds(3600), cache.MaxAge);
        Assert.False(cache.Private);
        Assert.False(cache.NoStore);
        Assert.False(cache.NoCache);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Public_sitemap_keeps_exact_canonical_inventory_localized_alternates_and_private_exclusions(string culture)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"/sitemap?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        Assert.Equal(ns + "urlset", xml.Root?.Name);
        var entries = xml.Root!.Elements(ns + "url").ToArray();
        Assert.Equal(27, entries.Length);
        Assert.Equal(IndexedPaths.Select(path => "https://www.maliev.com" + path),
            entries.Select(entry => entry.Element(ns + "loc")?.Value));
        for (var index = 0; index < entries.Length; index++)
        {
            var links = entries[index].Elements(xhtml + "link").ToArray();
            Assert.Equal(3, links.Length);
            Assert.Equal(new[] { "en", "th", "x-default" }, links.Select(link => link.Attribute("hreflang")?.Value));
            Assert.All(links, link => Assert.Equal("alternate", link.Attribute("rel")?.Value));
            var canonical = "https://www.maliev.com" + IndexedPaths[index];
            Assert.Equal(new[] { canonical + "?culture=en", canonical, canonical },
                links.Select(link => link.Attribute("href")?.Value));
        }

        Assert.Empty(xml.Descendants(ns + "lastmod"));
        var locations = entries.Select(entry => entry.Element(ns + "loc")!.Value).ToArray();
        foreach (var excluded in new[] { "/account", "/member", "/login", "/signup", "/quotation", "/cnc-machining" })
        {
            Assert.DoesNotContain("https://www.maliev.com" + excluded, locations);
        }
    }

    [Fact]
    public async Task Public_sitemap_xml_is_identical_across_cultures_and_caller_cookie_input()
    {
        using var client = CreateClient();
        using var english = await client.GetAsync("/sitemap?culture=en");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Sitemap?culture=th");
        request.Headers.Add("Cookie", "sitemap-test-marker=synthetic-caller-input");
        using var thai = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, english.StatusCode);
        Assert.Equal(HttpStatusCode.OK, thai.StatusCode);
        Assert.Equal(await english.Content.ReadAsStringAsync(), await thai.Content.ReadAsStringAsync());
        Assert.False(english.Headers.Contains("Set-Cookie"));
        Assert.False(thai.Headers.Contains("Set-Cookie"));
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = false
    });
}
