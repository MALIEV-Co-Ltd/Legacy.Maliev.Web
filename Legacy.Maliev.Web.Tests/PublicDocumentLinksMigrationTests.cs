using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicDocumentLinksMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public PublicDocumentLinksMigrationTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"))
            .CreateClient(new WebApplicationFactoryClientOptions
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
        Assert.DoesNotContain("Request.Query", model, StringComparison.Ordinal);
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
