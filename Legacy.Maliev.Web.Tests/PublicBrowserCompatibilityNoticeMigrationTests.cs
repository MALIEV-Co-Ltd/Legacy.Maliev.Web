using System.Net;
using System.Globalization;
using Legacy.Maliev.Web.Components.Layout;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicBrowserCompatibilityNoticeMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string MigrationMarker = "data-migration-component=\"public-browser-compatibility-notice\"";
    private readonly WebApplicationFactory<Program> factory;

    public PublicBrowserCompatibilityNoticeMigrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void PublicLayouts_UseLocalizedDisplayOnlyStaticBrowserCompatibilityComponent()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var layouts = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml"),
        };

        foreach (var layoutPath in layouts)
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.Contains("type=\"typeof(PublicBrowserCompatibilityNotice)\"", layout, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", layout, StringComparison.Ordinal);
            Assert.Contains("PublicBrowserCompatibilityNoticeDisplayModel.Create(Context)", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_BrowserCompatibilityCheckPartial", layout, StringComparison.Ordinal);
            Assert.True(
                layout.IndexOf("typeof(PublicNavigation)", StringComparison.Ordinal)
                < layout.IndexOf("typeof(PublicBrowserCompatibilityNotice)", StringComparison.Ordinal));
            Assert.True(
                layout.IndexOf("typeof(PublicBrowserCompatibilityNotice)", StringComparison.Ordinal)
                < layout.IndexOf("@RenderBody()", StringComparison.Ordinal));
        }

        var componentPath = Path.Combine(
            web,
            "Components",
            "Layout",
            "PublicBrowserCompatibilityNotice.razor");
        var modelPath = Path.Combine(
            web,
            "Components",
            "Layout",
            "PublicBrowserCompatibilityNoticeDisplayModel.cs");
        Assert.True(File.Exists(componentPath));
        Assert.True(File.Exists(modelPath));
        Assert.True(File.Exists(Path.Combine(
            web,
            "Resources",
            "Components",
            "Layout",
            "PublicBrowserCompatibilityNotice.th.resx")));
        Assert.False(File.Exists(Path.Combine(
            web,
            "Pages",
            "Shared",
            "_BrowserCompatibilityCheckPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(
            web,
            "Resources",
            "Pages",
            "Shared",
            "_BrowserCompatibilityCheckPartial.th.resx")));

        var component = File.ReadAllText(componentPath);
        Assert.DoesNotContain("HttpContext", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\" rel=\"noopener\"", component, StringComparison.Ordinal);

        var model = File.ReadAllText(modelPath);
        Assert.Contains("Contains(\"Trident\", StringComparison.Ordinal)", model, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", model, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", model, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstantQuotationLayout_TridentUserAgentRendersNoticeAndScopedBlazorBootstrap()
    {
        var html = await GetSourceAsync(
            "/instantquotation/3d-printing?culture=en",
            "Mozilla/5.0 Trident/7.0; rv:11.0");

        Assert.Equal(1, CountOccurrences(html, MigrationMarker));
        Assert.Contains("Please change your browser for best experience.", html, StringComparison.Ordinal);
        Assert.Contains("You are current browsing with: Mozilla/5.0 Trident/7.0; rv:11.0", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://www.mozilla.org/firefox\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://www.google.com/chrome\"", html, StringComparison.Ordinal);
        Assert.Contains("/_framework/blazor.web.js", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicLayout_TridentUserAgentRendersLocalizedThaiNoticeExactlyOnce()
    {
        var html = await GetSourceAsync("/legal?culture=th", "Mozilla/5.0 Trident/7.0; rv:11.0");
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Equal(1, CountOccurrences(html, MigrationMarker));
        Assert.Contains("กรุณาเปลี่ยน Browser เพื่อใช้เว็ปไซต์เรา", decoded, StringComparison.Ordinal);
        Assert.Contains("ตอนนี้คุณกำลังใช้: Mozilla/5.0 Trident/7.0; rv:11.0", decoded, StringComparison.Ordinal);
        Assert.Contains("กรุณาใช้", decoded, StringComparison.Ordinal);
        Assert.Contains("หรือ", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void ThaiComponentResource_ResolvesThroughTheTypedLocalizer()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("th");
            var localizer = factory.Services.GetRequiredService<IStringLocalizer<PublicBrowserCompatibilityNotice>>();

            var localized = localizer["Please change your browser for best experience."];

            Assert.False(localized.ResourceNotFound);
            Assert.Equal("กรุณาเปลี่ยน Browser เพื่อใช้เว็ปไซต์เรา", localized.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("Mozilla/5.0 Chrome/126.0.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 trident/7.0; rv:11.0")]
    public async Task CompatibleOrLowercaseUserAgent_DoesNotRenderNotice(string userAgent)
    {
        var publicHtml = await GetSourceAsync("/legal?culture=en", userAgent);
        var quotationHtml = await GetSourceAsync("/instantquotation/3d-printing?culture=en", userAgent);

        Assert.DoesNotContain(MigrationMarker, publicHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(MigrationMarker, quotationHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostileTridentUserAgent_IsHtmlEncodedAsText()
    {
        const string hostileUserAgent = "Mozilla/5.0 Trident/7.0 <script>alert(1)</script>";

        var html = await GetSourceAsync("/legal?culture=en", hostileUserAgent);

        Assert.Equal(1, CountOccurrences(html, MigrationMarker));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
    }

    private async Task<string> GetSourceAsync(string path, string userAgent)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Assert.True(request.Headers.TryAddWithoutValidation("User-Agent", userAgent));
        using var response = await client.SendAsync(request);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return source;
    }

    private static int CountOccurrences(string value, string search) =>
        value.Split(search, StringSplitOptions.None).Length - 1;

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
