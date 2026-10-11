using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Analytics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

public sealed class OpaqueAnalyticsEmptyConfigurationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EmptyConfiguration_PreservesFixedContainerAndProjectsOnlyConsentedOpaqueSubject(
        bool authenticated, bool consent)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "customer:42"),
                 new Claim(CustomerIdentityClaims.AnalyticsSubject, "opaque</script>subject"),
                 new Claim(ClaimTypes.Email, "private@example.test")],
                authenticated ? "server-owned-cookie" : null))
        };
        context.Features.Set<ITrackingConsentFeature>(new ConsentFeature(consent));
        var model = PublicGoogleTagManagerDisplayModel.Create(context, new TempDataDictionaryFactory(new EmptyTempDataProvider()));
        Assert.Equal(authenticated && consent, !string.IsNullOrEmpty(model.UserIdConfigurationJson));
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PublicGoogleTagManagerHead>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(PublicGoogleTagManagerHead.Model)] = model }))).ToHtmlString());
        Assert.Contains("https://www.googletagmanager.com/gtm.js?id=GTM-KHDDLVRR", html, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.test", html, StringComparison.Ordinal);
        Assert.DoesNotContain("opaque</script>subject", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\"user_id\":\"customer:42\"", html, StringComparison.Ordinal);
        if (authenticated && consent)
        {
            Assert.Contains($"window.gtag('set', {model.UserIdConfigurationJson});", html, StringComparison.Ordinal);
            Assert.Contains("opaque", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("opaque", html, StringComparison.Ordinal);
        }
    }

    private sealed class ConsentFeature(bool consent) : ITrackingConsentFeature
    {
        public bool IsConsentNeeded => true;
        public bool HasConsent => consent;
        public bool CanTrack => consent;
        public void GrantConsent() => throw new InvalidOperationException("The display model cannot grant consent.");
        public void WithdrawConsent() => throw new InvalidOperationException("The display model cannot withdraw consent.");
        public string CreateConsentCookie() => throw new InvalidOperationException("The GTM model cannot create a consent cookie.");
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
