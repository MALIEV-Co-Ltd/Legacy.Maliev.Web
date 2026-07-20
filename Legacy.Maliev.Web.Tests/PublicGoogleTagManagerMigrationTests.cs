using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Components.Analytics;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class PublicGoogleTagManagerMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicGoogleTagManagerMigrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void SharedLayouts_UseOneTypedStaticGtmModelForHeadAndBody()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var layouts = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml"),
            Path.Combine(web, "Areas", "Knowledges", "Pages", "Shared", "_LayoutKnowledges.cshtml"),
            Path.Combine(web, "Areas", "Member", "Pages", "Shared", "_LayoutMember.cshtml")
        };

        foreach (var layoutPath in layouts)
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.Contains("PublicGoogleTagManagerDisplayModel.Create(Context", layout, StringComparison.Ordinal);
            Assert.Contains("type=\"typeof(PublicGoogleTagManagerHead)\"", layout, StringComparison.Ordinal);
            Assert.Contains("type=\"typeof(PublicGoogleTagManagerBody)\"", layout, StringComparison.Ordinal);
            Assert.Equal(2, Regex.Matches(layout, "param-Model=\"@\\(googleTagManagerModel\\)\"").Count);
            Assert.DoesNotContain("_GoogleTagManagerPartial", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_GoogleTagManagerBodyPartial", layout, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(Path.Combine(web, "Components", "Analytics", "PublicGoogleTagManagerHead.razor")));
        Assert.True(File.Exists(Path.Combine(web, "Components", "Analytics", "PublicGoogleTagManagerBody.razor")));
        Assert.True(File.Exists(Path.Combine(web, "Components", "Analytics", "PublicGoogleTagManagerDisplayModel.cs")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_GoogleTagManagerPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_GoogleTagManagerBodyPartial.cshtml")));
    }

    [Fact]
    public async Task DefaultConsent_RendersDeniedConsentModeAndSuppressesNoscript()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.Contains("'ad_storage': consentState", source, StringComparison.Ordinal);
        Assert.Contains("'analytics_storage': consentState", source, StringComparison.Ordinal);
        Assert.Contains("'ad_user_data': consentState", source, StringComparison.Ordinal);
        Assert.Contains("'ad_personalization': consentState", source, StringComparison.Ordinal);
        Assert.Contains("'wait_for_update': 500", source, StringComparison.Ordinal);
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
        Assert.Contains("pendingEvents", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptedConsent_RendersGrantedConsentAndNoscriptContainer()
    {
        using var client = CreateClient();
        var initialSource = WebUtility.HtmlDecode(await client.GetStringAsync("/?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initialSource).Groups["cookie"].Value;
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
    public void QueuedCompletedUpload_RendersOnlyControlledLeadAndCompletionEvents()
    {
        var leadEvent = new LeadAnalyticsEvent(
            "quotation_request",
            "cnc_machining",
            "quotation-714",
            hasFiles: true,
            fileUploadCompleted: true);
        var provider = new PreloadedTempDataProvider(new Dictionary<string, object>
        {
            ["Maliev.LeadAnalyticsEvent"] = JsonSerializer.Serialize(leadEvent)
        });
        var model = PublicGoogleTagManagerDisplayModel.Create(
            new DefaultHttpContext(),
            new TempDataDictionaryFactory(provider));

        Assert.Contains("window.malievAnalytics.emit", model.QueuedEventScript, StringComparison.Ordinal);
        Assert.Contains("\"event\":\"request_quote\"", model.QueuedEventScript, StringComparison.Ordinal);
        Assert.Contains("\"event\":\"file_upload_complete\"", model.QueuedEventScript, StringComparison.Ordinal);
        Assert.Contains("\"service\":\"cnc_machining\"", model.QueuedEventScript, StringComparison.Ordinal);
        Assert.Contains("\"transaction_id\":\"quotation-714\"", model.QueuedEventScript, StringComparison.Ordinal);
        Assert.DoesNotContain("email", model.QueuedEventScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QueuedEvent_SaveFailureSuppressesEmissionWithoutBreakingThePage()
    {
        var leadEvent = new LeadAnalyticsEvent(
            "quotation_request",
            "3d_printing",
            "quotation-724",
            hasFiles: true,
            fileUploadCompleted: true);
        var provider = new PreloadedTempDataProvider(new Dictionary<string, object>
        {
            ["Maliev.LeadAnalyticsEvent"] = JsonSerializer.Serialize(leadEvent)
        }, throwOnSave: true);

        var model = PublicGoogleTagManagerDisplayModel.Create(
            new DefaultHttpContext(),
            new TempDataDictionaryFactory(provider));

        Assert.Empty(model.QueuedEventScript);
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

    [GeneratedRegex("data-cookie-string=\"(?<cookie>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ConsentCookieRegex();

    private sealed class PreloadedTempDataProvider(
        Dictionary<string, object> values,
        bool throwOnSave = false) : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            if (throwOnSave)
            {
                throw new InvalidOperationException("Test TempData unavailable.");
            }
        }
    }
}
