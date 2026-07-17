using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicContactChannelAnalyticsMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicContactChannelAnalyticsMigrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void SharedLayouts_UseDisplayOnlyStaticContactAnalyticsComponent()
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
            Assert.Contains("type=\"typeof(PublicContactChannelAnalytics)\"", layout, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_ContactChannelAnalyticsPartial", layout, StringComparison.Ordinal);
        }

        var component = Path.Combine(web, "Components", "Analytics", "PublicContactChannelAnalytics.razor");
        Assert.True(File.Exists(component));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_ContactChannelAnalyticsPartial.cshtml")));
    }

    [Fact]
    public async Task PublicRoute_RendersCompleteConsentSafeContactAnalyticsContract()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/services/cnc-machining?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.emit(contactEvent)", source, StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.emit(reviewEvent)", source, StringComparison.Ordinal);
        Assert.Contains("eventName: 'phone_click'", source, StringComparison.Ordinal);
        Assert.Contains("eventName: 'email_click'", source, StringComparison.Ordinal);
        Assert.Contains("eventName: 'line_click'", source, StringComparison.Ordinal);
        Assert.Contains("eventName: 'whatsapp_click'", source, StringComparison.Ordinal);
        Assert.Contains("eventName: 'instagram_click'", source, StringComparison.Ordinal);
        Assert.Contains("eventName: 'youtube_click'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("eventName: 'messenger_click'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("eventName: 'facebook_click'", source, StringComparison.Ordinal);
        Assert.Contains("event: contact.eventName", source, StringComparison.Ordinal);
        Assert.Contains("event: 'maliev_review_link_click'", source, StringComparison.Ordinal);
        Assert.Contains("path === '/ti/p/@maliev'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("path === '/ti/p/@@maliev'", source, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", source, StringComparison.Ordinal);
        Assert.Contains("window.setTimeout", source, StringComparison.Ordinal);
        Assert.Contains("}, 150);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("window.dataLayer.push(contactEvent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

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
