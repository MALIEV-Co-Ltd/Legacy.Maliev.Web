using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks account/member indexing through unchanged routing and security middleware.</summary>
public sealed class AccountUtilityIndexingHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task AccountIndex_ActiveAndRetainedRenderersHaveExactlyOneFolderDirective(string culture, bool retained)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:AccountIndex", (!retained).ToString()));
        using var client = Client(host);
        using var response = await client.GetAsync($"/Account?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Contains(retained ? "data-migration-renderer=" : "data-migration-route-owner=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("action=\"/Account/Logout\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", html, StringComparison.OrdinalIgnoreCase);
        AssertHeader(response);
        AssertRobots(html, "noindex, follow");
    }

    [Theory]
    [InlineData("/member")]
    [InlineData("/MEMBER/Account")]
    [InlineData("/member/Account/Manage/Profile")]
    public async Task MemberAnonymousRedirect_UsesOriginalFolderHeaderWithoutRenderingPrivateContent(string path)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("data-migration-component=\"member-profile-content\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", body, StringComparison.OrdinalIgnoreCase);
        AssertHeader(response);
    }

    [Theory]
    [InlineData("/member/unrecognized-utility")]
    [InlineData("/MEMBER/unrecognized-utility")]
    public async Task MemberUnrecognizedUtility_ReexecutionPreservesOriginalFolderHeader(string path)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("noindex", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        AssertHeader(response);
    }

    [Fact]
    public async Task MemberHead_RetainsAnonymousChallengeAndFolderHeader()
    {
        using var client = Client(factory);
        using var request = new HttpRequestMessage(HttpMethod.Head, "/Member/Account");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        AssertHeader(response);
    }

    [Fact]
    public async Task MemberAnonymousPost_IsRejectedBeforePrivateMutationAndRetainsFolderHeader()
    {
        using var client = Client(factory);
        using var response = await client.PostAsync("/Member/Account/Manage/ChangePassword", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.DoesNotContain("member-change-password-content", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        AssertHeader(response);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task CredentialUtility_RetainsStricterMetadataAndExistingFolderHeader(string culture)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"/account/forgotpassword?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertHeader(response);
        AssertRobots(await response.Content.ReadAsStringAsync(), "noindex, nofollow");
    }

    [Theory]
    [InlineData("/membership", HttpStatusCode.NotFound)]
    [InlineData("/member-account", HttpStatusCode.NotFound)]
    [InlineData("/about?culture=en", HttpStatusCode.OK)]
    public async Task NonMemberDocument_DoesNotAcquirePrivateFolderHeader(string path, HttpStatusCode status)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(status, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static void AssertHeader(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("X-Robots-Tag", out var values), "Original utility folder must retain its indexing header.");
        Assert.Equal("noindex, follow", Assert.Single(values!));
    }

    private static void AssertRobots(string html, string directive)
    {
        var tags = Regex.Matches(html, "<meta\\b[^>]*>", RegexOptions.IgnoreCase)
            .Where(tag => Regex.IsMatch(tag.Value, "\\bname\\s*=\\s*[\"']robots[\"']", RegexOptions.IgnoreCase)).ToArray();
        var tag = Assert.Single(tags);
        var value = Regex.Match(tag.Value, "\\bcontent\\s*=\\s*[\"'](?<value>[^\"']*)[\"']", RegexOptions.IgnoreCase);
        Assert.True(value.Success);
        Assert.Equal(directive.Replace(" ", "", StringComparison.Ordinal), value.Groups["value"].Value.Replace(" ", "", StringComparison.Ordinal));
    }
}
