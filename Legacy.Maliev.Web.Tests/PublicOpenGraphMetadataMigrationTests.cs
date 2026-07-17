using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicOpenGraphMetadataMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public PublicOpenGraphMetadataMigrationTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://untrusted-request-host.example")
            });
    }

    [Fact]
    public void SharedPublicLayouts_UseDisplayOnlyStaticOpenGraphComponent()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var layoutPaths = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml")
        };

        foreach (var layoutPath in layoutPaths)
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.Contains("type=\"typeof(PublicOpenGraphMetadata)\"", layout, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", layout, StringComparison.Ordinal);
            Assert.Contains("PublicOpenGraphMetadataDisplayModel.Create(Context", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_FacebookOpenGraphPartial", layout, StringComparison.Ordinal);
        }

        var componentPath = Path.Combine(web, "Components", "Layout", "PublicOpenGraphMetadata.razor");
        var modelPath = Path.Combine(web, "Components", "Layout", "PublicOpenGraphMetadataDisplayModel.cs");
        Assert.True(File.Exists(componentPath));
        Assert.True(File.Exists(modelPath));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_FacebookOpenGraphPartial.cshtml")));

        var component = File.ReadAllText(componentPath);
        Assert.Contains("data-migration-component=\"public-open-graph-metadata\"", component, StringComparison.Ordinal);
        Assert.Contains("Model.Image", component, StringComparison.Ordinal);
        Assert.Contains("Model.Title", component, StringComparison.Ordinal);
        Assert.Contains("Model.Description", component, StringComparison.Ordinal);
        Assert.Contains("Model.Locale", component, StringComparison.Ordinal);
        Assert.Contains("Model.Url", component, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        var model = File.ReadAllText(modelPath);
        Assert.Contains("CanonicalUrlPolicy.GetLocalizedUrl", model, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Host", model, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Query", model, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", model, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", model, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "Legal information | MALIEV", "https://www.maliev.com/legal?culture=en")]
    [InlineData("th", "ข้อมูลทางกฎหมาย | MALIEV", "https://www.maliev.com/legal")]
    public async Task OpenGraphMetadata_PreservesValuesAndUsesCanonicalLocalizedUrl(
        string culture,
        string title,
        string canonicalUrl)
    {
        using var response = await client.GetAsync($"/legal?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-open-graph-metadata\"", source, StringComparison.Ordinal);
        Assert.Equal(1, CountMeta(source, "og:image"));
        Assert.Equal(1, CountMeta(source, "og:image:alt", "Online Manufacturing - MALIEV"));
        Assert.Equal(1, CountMeta(source, "og:image:height", "239"));
        Assert.Equal(1, CountMeta(source, "og:image:width", "159"));
        Assert.Equal(1, CountMeta(source, "og:locale", culture));
        Assert.Equal(1, CountMeta(source, "og:title", title));
        Assert.Equal(1, CountMeta(source, "og:description"));
        Assert.Equal(1, CountMeta(source, "og:type", "website"));
        Assert.Equal(1, CountMeta(source, "og:url", canonicalUrl));
        Assert.Equal(1, CountMeta(source, "og:site_name", "Maliev Manufacturing"));
        Assert.Equal(0, CountMeta(source, "fb:app_id"));
        Assert.DoesNotContain("content=", GetMeta(source, "og:image"), StringComparison.Ordinal);
        Assert.DoesNotContain("content=", GetMeta(source, "og:description"), StringComparison.Ordinal);
        Assert.DoesNotContain("untrusted-request-host.example", ExtractOpenGraphMetadata(source), StringComparison.Ordinal);
        Assert.DoesNotContain("tracking=excluded", ExtractOpenGraphMetadata(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountMeta(string source, string property, string? content = null)
    {
        var contentLookahead = content is null
            ? string.Empty
            : $"(?=[^>]*content=\"{Regex.Escape(content)}\")";

        return Regex.Matches(
            source,
            $"<meta(?=[^>]*property=\"{Regex.Escape(property)}\"){contentLookahead}[^>]*>",
            RegexOptions.CultureInvariant).Count;
    }

    private static string GetMeta(string source, string property) => Regex.Match(
        source,
        $"<meta(?=[^>]*property=\"{Regex.Escape(property)}\")[^>]*>",
        RegexOptions.CultureInvariant).Value;

    private static string ExtractOpenGraphMetadata(string source) => string.Join(
        Environment.NewLine,
        Regex.Matches(source, "<meta[^>]+property=\"(?:og|fb):[^\"]+\"[^>]*>", RegexOptions.CultureInvariant)
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
