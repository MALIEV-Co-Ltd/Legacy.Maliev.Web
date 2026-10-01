using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises the source account-folder indexing header through the real Web pipeline.</summary>
public sealed class AccountNoindexHeaderHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/account/login?culture=en", "en", "noindex, follow")]
    [InlineData("/account/login?culture=th", "th", "noindex, follow")]
    [InlineData("/ACCOUNT/Login?culture=en", "en", "noindex, follow")]
    [InlineData("/account/signup?culture=en", "en", "noindex, follow")]
    [InlineData("/account/forgotpassword?culture=en", "en", "noindex, nofollow")]
    public async Task AccountDocument_PreservesLocalizedMetadataAndAddsLiteralFolderHeader(
        string path, string language, string robotsMeta)
    {
        using var client = Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"<html lang=\"{language}\"", document, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"robots\" content=\"{robotsMeta}\"", document, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", document, StringComparison.Ordinal);
        AssertAccountHeader(response);
    }

    [Fact]
    public async Task AccountHead_PreservesStatusAndAddsFolderHeader()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/account/login?culture=en");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertAccountHeader(response);
    }

    [Theory]
    [InlineData("/account/login?handler=Login")]
    [InlineData("/ACCOUNT/ForgotPassword?handler=ForgotPassword")]
    public async Task AccountPost_MissingAntiforgeryRetainsEarlyRejectionAndFolderHeader(string path)
    {
        using var client = Client();
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Email"] = "owned-no-delivery@example.invalid" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        AssertAccountHeader(response);
    }

    [Theory]
    [InlineData("/account/unrecognized-utility", HttpStatusCode.NotFound)]
    [InlineData("/ACCOUNT/unrecognized-utility", HttpStatusCode.NotFound)]
    [InlineData("/account", HttpStatusCode.OK)]
    public async Task AccountFolderBoundary_IncludesFolderRootAndStatusReexecutedNotFound(
        string path, HttpStatusCode expected)
    {
        using var client = Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var document = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(document);
        if (expected == HttpStatusCode.NotFound) Assert.Contains("noindex", document, StringComparison.OrdinalIgnoreCase);
        AssertAccountHeader(response);
    }

    [Theory]
    [InlineData("/about?culture=en", HttpStatusCode.OK)]
    [InlineData("/robots.txt", HttpStatusCode.OK)]
    [InlineData("/accounting/login", HttpStatusCode.NotFound)]
    [InlineData("/account-login", HttpStatusCode.NotFound)]
    [InlineData("/unrecognized-public-route", HttpStatusCode.NotFound)]
    public async Task NonAccountOrPrefixOnlyPath_DoesNotAcquireAccountFolderHeader(
        string path, HttpStatusCode expected)
    {
        using var client = Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static void AssertAccountHeader(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("X-Robots-Tag", out var values),
            "Account responses must carry the source folder-level X-Robots-Tag even before document rendering.");
        Assert.Equal("noindex, follow", Assert.Single(values!));
    }
}
