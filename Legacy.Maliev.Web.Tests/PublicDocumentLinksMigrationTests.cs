using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicDocumentLinksMigrationTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly TestingWebApplicationFactory factory;
    private readonly HttpClient client;

    public PublicDocumentLinksMigrationTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public void PublicLayouts_UseDisplayOnlyStaticDocumentLinksComponent()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var layoutPaths = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml"),
            Path.Combine(web, "Areas", "Knowledges", "Pages", "Shared", "_LayoutKnowledges.cshtml")
        };

        foreach (var layoutPath in layoutPaths)
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.Contains("type=\"typeof(PublicDocumentLinks)\"", layout, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", layout, StringComparison.Ordinal);
            Assert.Contains("PublicDocumentLinksDisplayModel.Create(Context)", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_RelCanonicalPartial", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_RelAlternatePartial", layout, StringComparison.Ordinal);
        }

        var componentPath = Path.Combine(web, "Components", "Layout", "PublicDocumentLinks.razor");
        var modelPath = Path.Combine(web, "Components", "Layout", "PublicDocumentLinksDisplayModel.cs");
        Assert.True(File.Exists(componentPath));
        Assert.True(File.Exists(modelPath));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_RelCanonicalPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_RelAlternatePartial.cshtml")));

        var component = File.ReadAllText(componentPath);
        Assert.Contains("data-migration-component=\"public-document-links\"", component, StringComparison.Ordinal);
        Assert.Contains("Model.CanonicalUrl", component, StringComparison.Ordinal);
        Assert.Contains("Model.EnglishUrl", component, StringComparison.Ordinal);
        Assert.Contains("Model.ThaiUrl", component, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        var model = File.ReadAllText(modelPath);
        Assert.Contains("CanonicalUrlPolicy.GetLocalizedUrl", model, StringComparison.Ordinal);
        Assert.Contains("context.Request.Query[\"culture\"]", model, StringComparison.Ordinal);
        Assert.Contains("CultureInfo.CurrentUICulture", model, StringComparison.Ordinal);
        Assert.Contains("path.StartsWithSegments(\"/account\", StringComparison.OrdinalIgnoreCase)", model, StringComparison.Ordinal);
        Assert.Contains("path.StartsWithSegments(\"/member\", StringComparison.OrdinalIgnoreCase)", model, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", model, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", model, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/", "en", "https://www.maliev.com/?culture=en", "https://www.maliev.com/?culture=en", "https://www.maliev.com/")]
    [InlineData("/legal", "th", "https://www.maliev.com/legal", "https://www.maliev.com/legal?culture=en", "https://www.maliev.com/legal")]
    [InlineData("/services/3d-printing", "en", "https://www.maliev.com/services/3d-printing?culture=en", "https://www.maliev.com/services/3d-printing?culture=en", "https://www.maliev.com/services/3d-printing")]
    [InlineData("/knowledges/guidelines", "th", "https://www.maliev.com/knowledges/guidelines", "https://www.maliev.com/knowledges/guidelines?culture=en", "https://www.maliev.com/knowledges/guidelines")]
    public async Task PublicDocumentLinks_PreserveCanonicalAndHreflangContract(
        string route,
        string culture,
        string canonicalUrl,
        string englishUrl,
        string thaiUrl)
    {
        using var response = await client.GetAsync($"{route}?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-document-links\"", source, StringComparison.Ordinal);
        Assert.Equal(1, CountLink(source, "canonical", canonicalUrl));
        Assert.Equal(1, CountAlternate(source, "en", englishUrl));
        Assert.Equal(1, CountAlternate(source, "th", thaiUrl));
        Assert.Equal(1, CountAlternate(source, "x-default", thaiUrl));
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, "en", "")]
    [InlineData(true, "th", "")]
    [InlineData(false, "en", "")]
    [InlineData(false, "th", "")]
    [InlineData(true, "en", "/web-source")]
    [InlineData(true, "th", "/web-source")]
    [InlineData(false, "en", "/web-source")]
    [InlineData(false, "th", "/web-source")]
    public async Task Favicons_BothRenderersVersionEveryOwnedAssetAgainstItsResponseBytes(
        bool active, string culture, string pathBase)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Home", active ? "true" : "false");
            if (pathBase.Length > 0)
                builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(
                    new FaviconPathBaseStartupFilter(pathBase)));
        });
        using var faviconClient = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await faviconClient.GetAsync(pathBase + "/?culture=" + culture, deadline.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(deadline.Token));
        Assert.Equal(active, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/favicon.webp"] = "4873940545d64653135bcbf081bc8ed0ea773c618b75a7f9c63c8207ce74a936",
            ["/favicon-16x16.webp"] = "d3bb9eddb84df6c8014e335e317399bdb80956844563e62c36d5e09a692948ea",
            ["/favicon-32x32.webp"] = "59366423e121f91259ac8099ffb71839f4fe551ab1b800bf7fa1d98611726e2f",
            ["/apple-touch-icon.webp"] = "0f4444eb5c23dcaae9f9c99ec4eb6b102fb8e07d979d61b7a01a77d92783552e",
        };
        var links = Regex.Matches(html, "<link\\b[^>]*>", RegexOptions.CultureInvariant).Cast<Match>()
            .Where(link => Regex.Match(link.Value, "rel=\"([^\"]+)\"", RegexOptions.CultureInvariant)
                .Groups[1].Value is "shortcut icon" or "icon" or "apple-touch-icon").ToArray();
        Assert.Equal(5, links.Length);
        var paths = new List<string>();
        foreach (var link in links)
        {
            var href = Regex.Match(link.Value, "href=\"([^\"]+)\"", RegexOptions.CultureInvariant).Groups[1].Value;
            var asset = new Uri(faviconClient.BaseAddress!, href);
            Assert.Equal(faviconClient.BaseAddress!.Authority, asset.Authority);
            Assert.StartsWith(pathBase + "/", asset.AbsolutePath, StringComparison.Ordinal);
            var assetPath = asset.AbsolutePath[pathBase.Length..];
            Assert.True(expected.TryGetValue(assetPath, out var hash), "Favicon URLs must stay owned.");
            var version = Convert.ToBase64String(Convert.FromHexString(hash!))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal("?v=" + version, asset.Query);
            using var assetResponse = await faviconClient.GetAsync(asset, deadline.Token);
            Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
            Assert.Equal("image/webp", assetResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(hash, Convert.ToHexString(
                SHA256.HashData(await assetResponse.Content.ReadAsByteArrayAsync(deadline.Token))).ToLowerInvariant());
            paths.Add(assetPath);
        }
        Assert.Equal(2, paths.Count(path => path == "/favicon.webp"));
        foreach (var path in expected.Keys) Assert.Contains(path, paths);
    }

    private sealed class FaviconPathBaseStartupFilter(string pathBase) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UsePathBase(pathBase);
            next(app);
        };
    }

    private static int CountLink(string source, string relation, string url) =>
        Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"{Regex.Escape(relation)}\")(?=[^>]*href=\"{Regex.Escape(url)}\")[^>]*>",
            RegexOptions.CultureInvariant).Count;

    private static int CountAlternate(string source, string language, string url) =>
        Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"alternate\")(?=[^>]*href=\"{Regex.Escape(url)}\")(?=[^>]*hreflang=\"{Regex.Escape(language)}\")[^>]*>",
            RegexOptions.CultureInvariant).Count;

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
