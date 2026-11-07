using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Components.Layout;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class LineContactAssetHttpTests : IAsyncLifetime
{
    private OwnedWebSurfaceParent? ownedParent = new();
    private OwnedWebSurfaceParent factory => ownedParent ?? throw new ObjectDisposedException(nameof(LineContactAssetHttpTests));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        var parent = ownedParent;
        try
        {
            if (parent is not null) await parent.DisposeAsync();
        }
        finally { ownedParent = null; }
    }
    [Theory]
    [InlineData("/contact/line")]
    [InlineData("/CONTACT/LINE/")]
    public void FriendshipRoute_SelectsSourceInquiryAssets(string route)
    {
        Assert.Contains("route-inquiry.css", PublicRouteAssets.GetStyles(route));
        Assert.Contains("route-inquiry.js", PublicRouteAssets.GetScripts(route));
    }

    [Theory]
    [InlineData("en", "Add MALIEV on LINE")]
    [InlineData("th", "เพิ่มเพื่อน MALIEV บน LINE")]
    public async Task RealLineRoute_ServesOwnedInquiryAssetsAndKeepsNoindexFallback(string culture, string heading)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var response = await client.GetAsync($"/contact/line?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(heading, html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,follow\"", html, StringComparison.Ordinal);
        var style = Regex.Match(html, "href=\"([^\"]*/dist/route-inquiry\\.css[^\"]*)\"");
        Assert.True(style.Success, "The live LINE route must select its source inquiry stylesheet.");
        using var stylesheet = await client.GetAsync(style.Groups[1].Value);
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Contains("text/css", stylesheet.Content.Headers.ContentType?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(await stylesheet.Content.ReadAsStringAsync()));
        var script = Regex.Match(html, "src=\"([^\"]*/dist/route-inquiry\\.js[^\"]*)\"");
        Assert.True(script.Success, "The live LINE route must select its source inquiry script.");
        using var javascript = await client.GetAsync(script.Groups[1].Value);
        Assert.Equal(HttpStatusCode.OK, javascript.StatusCode);
        Assert.Contains(javascript.Content.Headers.ContentType?.MediaType ?? string.Empty,
            new[] { "text/javascript", "application/javascript" });
        Assert.False(string.IsNullOrWhiteSpace(await javascript.Content.ReadAsStringAsync()));
        Assert.DoesNotContain("https://static.line-scdn.net/liff/edge/2/sdk.js", html, StringComparison.Ordinal);
    }
}
