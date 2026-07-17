using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicCookieConsentMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicCookieConsentMigrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void SharedLayouts_UseDisplayOnlyStaticCookieConsentComponent()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var layoutPaths = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml"),
            Path.Combine(web, "Areas", "Knowledges", "Pages", "Shared", "_LayoutKnowledges.cshtml"),
            Path.Combine(web, "Areas", "Member", "Pages", "Shared", "_LayoutMember.cshtml")
        };

        foreach (var layoutPath in layoutPaths)
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.Contains("type=\"typeof(PublicCookieConsent)\"", layout, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", layout, StringComparison.Ordinal);
            Assert.Contains("PublicCookieConsentDisplayModel.Create(Context)", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_CookieConsentPartial", layout, StringComparison.Ordinal);
        }

        var componentPath = Path.Combine(web, "Components", "Layout", "PublicCookieConsent.razor");
        var modelPath = Path.Combine(web, "Components", "Layout", "PublicCookieConsentDisplayModel.cs");
        Assert.True(File.Exists(componentPath));
        Assert.True(File.Exists(modelPath));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_CookieConsentPartial.cshtml")));

        var component = File.ReadAllText(componentPath);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", component, StringComparison.Ordinal);
        Assert.Contains("window.gtag('consent', 'update'", component, StringComparison.Ordinal);
        Assert.Contains("ad_storage: state", component, StringComparison.Ordinal);
        Assert.Contains("analytics_storage: state", component, StringComparison.Ordinal);
        Assert.Contains("ad_user_data: state", component, StringComparison.Ordinal);
        Assert.Contains("ad_personalization: state", component, StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.setConsent(state)", component, StringComparison.Ordinal);
        Assert.Contains("maliev_tracking_consent=denied", component, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        var model = File.ReadAllText(modelPath);
        Assert.Contains("ITrackingConsentFeature", model, StringComparison.Ordinal);
        Assert.Contains("CreateConsentCookie", model, StringComparison.Ordinal);
        Assert.Contains("maliev_tracking_consent", model, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", model, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", model, StringComparison.OrdinalIgnoreCase);

        Assert.True(File.Exists(Path.Combine(web, "Resources", "Components", "Layout", "PublicCookieConsent.th.resx")));
        Assert.False(File.Exists(Path.Combine(web, "Resources", "Pages", "Shared", "_CookieConsentPartial.th.resx")));
    }

    [Theory]
    [InlineData("en", "Your privacy choices", "Reject optional cookies", "Accept cookies", "Learn more about our privacy policy")]
    [InlineData("th", "ตัวเลือกความเป็นส่วนตัวของคุณ", "ปฏิเสธคุกกี้ที่ไม่จำเป็น", "ยอมรับคุกกี้", "เรียนรู้เพิ่มเติมเกี่ยวกับนโยบายความเป็นส่วนตัวของเรา")]
    public async Task CookieConsent_RendersLocalizedAccessibleConsentModeBridge(
        string culture,
        string heading,
        string rejectLabel,
        string acceptLabel,
        string privacyLabel)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"/legal?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"region\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"privacy-consent-title\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains($">{rejectLabel}<", source, StringComparison.Ordinal);
        Assert.Contains($">{acceptLabel}<", source, StringComparison.Ordinal);
        Assert.Contains($">{privacyLabel}<", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/privacypolicy\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-cookie-string=\"", source, StringComparison.Ordinal);
        Assert.Contains("window.gtag('consent', 'update'", source, StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.setConsent(state)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordedDenial_SuppressesConsentBannerAndBridge()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/legal?culture=en");
        request.Headers.Add("Cookie", "maliev_tracking_consent=denied");
        using var response = await client.SendAsync(request);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("maliev_tracking_consent=denied; Path=/", source, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

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
