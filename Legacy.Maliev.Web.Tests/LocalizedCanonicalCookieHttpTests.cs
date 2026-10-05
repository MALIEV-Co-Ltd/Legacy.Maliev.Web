using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Source-bound culture-cookie normalization through the actual Web HTTP pipeline.</summary>
public sealed class LocalizedCanonicalCookieHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string EnglishCookie = ".AspNetCore.Culture=c%3Den%7Cuic%3Den";

    [Theory]
    [InlineData(true, "GetEstimate")]
    [InlineData(true, "GetOrderTotal")]
    [InlineData(false, "GetEstimate")]
    [InlineData(false, "GetOrderTotal")]
    public async Task EnglishCookie_RecognizedLegacyJsonHandler_RetainsJsonWireWithoutDocumentRedirect(
        bool blazorRoute, string handler)
    {
        await using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:InstantQuotation", blazorRoute.ToString());
            if (!blazorRoute)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IInstantQuotationSubmissionService>();
                    services.AddSingleton<IInstantQuotationSubmissionService, UnusedSubmissionService>();
                });
            }
        });
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var query = handler == "GetEstimate"
            ? "&material=PLA&dimensionZ=30&volume=20000&footprint=400&quantity=1&currency=THB"
            : "&processes=fdm%2Cresin&subtotals=1200%2C1800&totalWeightGrams=500&totalBoundingCm3=2000&currency=THB";
        using var request = Request("GET", "/InstantQuotation/3D-Printing?handler=" + handler + query, EnglishCookie);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("THB", json.RootElement.GetProperty("currency").GetString());
        if (handler == "GetEstimate") Assert.Equal("fdm", json.RootElement.GetProperty("process").GetString());
        else Assert.Equal(3_000, json.RootElement.GetProperty("printing").GetDouble(), 2);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("HEAD", "")]
    [InlineData("GET", "?handler=Unknown")]
    [InlineData("GET", "?handler=GetEstimate&handler=GetOrderTotal")]
    public async Task EnglishCookie_QuotationDocumentOrUnrecognizedHandler_StillNormalizesCulture(
        string method, string query)
    {
        using var client = Client("https://localhost");
        using var request = Request(method, "/InstantQuotation/3D-Printing" + query, EnglishCookie);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/InstantQuotation/3D-Printing" + query + (query.Length == 0 ? "?" : "&") + "culture=en",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task EnglishCookie_RecognizedJsonHandler_RetainsPriorPublicHostNormalizationOnly()
    {
        using var client = Client("https://maliev.com");
        using var request = Request("GET", "/InstantQuotation/3D-Printing?handler=GetEstimate&material=PLA", EnglishCookie);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://www.maliev.com/InstantQuotation/3D-Printing?handler=GetEstimate&material=PLA",
            response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("GET", "https://localhost", "/about", "/about?culture=en")]
    [InlineData("HEAD", "https://localhost", "/about", "/about?culture=en")]
    [InlineData("GET", "https://www.maliev.com", "/about?ref=guide", "https://www.maliev.com/about?ref=guide&culture=en")]
    [InlineData("HEAD", "https://maliev.com", "/3d-scanning/?ref=map", "https://www.maliev.com/services/3d-scanning?ref=map&culture=en")]
    [InlineData("GET", "https://localhost", "/about?culture=invalid&tag=a&tag=b", "/about?tag=a&tag=b&culture=en")]
    [InlineData("GET", "https://localhost", "/about?culture=en&culture=th&ref=x", "/about?ref=x&culture=en")]
    [InlineData("GET", "https://localhost", "/about?CULTURE=invalid&ref=x", "/about?ref=x&culture=en")]
    [InlineData("GET", "https://unconfigured.example", "/ABOUT/?ref=x", "/about?ref=x&culture=en")]
    public async Task EnglishCookie_PublicRead_NormalizesHostPathAndCultureInOneHop(
        string method, string origin, string path, string expected)
    {
        using var client = Client(origin);
        using var request = Request(method, path, EnglishCookie);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
        if (method == "HEAD") Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task EnglishCookie_FollowedNormalizedUrl_IsEnglishWithOneCanonicalAndStableAlternates()
    {
        using var client = Client("https://localhost");
        using var request = Request("GET", "/about?ref=guide", EnglishCookie);
        using var first = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MovedPermanently, first.StatusCode);
        Assert.Equal("/about?ref=guide&culture=en", first.Headers.Location?.OriginalString);
        using var followed = Request("GET", first.Headers.Location!.OriginalString, EnglishCookie);
        using var response = await client.SendAsync(followed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("<html lang=\"en\"", document, StringComparison.Ordinal);
        Assert.Contains("From a family workshop to connected manufacturing.", document, StringComparison.Ordinal);
        AssertDocumentLinks(document, "https://www.maliev.com/about?culture=en");
        Assert.DoesNotContain("ref=guide", DocumentLinks(document), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?culture=th", "th", "https://www.maliev.com/about")]
    [InlineData("?culture=TH&ref=x", "th", "https://www.maliev.com/about")]
    [InlineData("?culture=en", "en", "https://www.maliev.com/about?culture=en")]
    [InlineData("?culture=EN&ref=x", "en", "https://www.maliev.com/about?culture=en")]
    public async Task ExplicitSupportedQuery_OverridesCookieWithoutRedirect(
        string query, string language, string canonical)
    {
        using var client = Client("https://localhost");
        using var request = Request("GET", "/about" + query, EnglishCookie);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"<html lang=\"{language}\"", document, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "From a family workshop to connected manufacturing."
            : "จากเวิร์กช็อปของครอบครัว สู่ระบบการผลิตที่เชื่อมต่อกัน", document, StringComparison.Ordinal);
        AssertDocumentLinks(document, canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(".AspNetCore.Culture=malformed")]
    [InlineData(".AspNetCore.Culture=c%3Dth%7Cuic%3Dth")]
    [InlineData(".AspNetCore.Culture=c%3Dfr%7Cuic%3Dfr")]
    [InlineData(".AspNetCore.Culture=")]
    public async Task MissingMalformedOrNonEnglishCookie_DoesNotCreateEnglishRedirect(string? cookie)
    {
        using var client = Client("https://localhost");
        using var request = Request("GET", "/about", cookie);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("<html lang=\"th\"", document, StringComparison.Ordinal);
        AssertDocumentLinks(document, "https://www.maliev.com/about");
    }

    [Theory]
    [InlineData("/account/login", HttpStatusCode.OK)]
    [InlineData("/not-a-public-route", HttpStatusCode.NotFound)]
    public async Task EnglishCookie_NonPublicDocument_RetainsStatusAndNoindex(string path, HttpStatusCode expected)
    {
        using var client = Client("https://localhost");
        using var request = Request("GET", path, EnglishCookie);
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("noindex", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnglishCookie_MutationWithoutAntiforgery_IsRejectedRatherThanRedirected()
    {
        using var client = Client("https://localhost");
        using var request = Request("POST", "/?handler=SetLanguage&returnUrl=~/about", EnglishCookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["culture"] = "th" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("not-a-culture", "https://attacker.example/", "/", "c%3Dth%7Cuic%3Dth")]
    [InlineData("EN", "/about?tracking=excluded", "/about?culture=en", "c%3Den%7Cuic%3Den")]
    public async Task ActualLanguagePost_PreservesSafeLocalReturnAndNormalizedCookie(
        string culture, string returnUrl, string location, string cookieValue)
    {
        using var client = Client("https://localhost");
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/?culture=th"));
        var form = Regex.Match(initial, "<form[^>]*class=\"maliev-language-form\"[^>]*action=\"(?<action>[^\"]+)\"[\\s\\S]*?</form>");
        Assert.True(form.Success);
        var token = Regex.Match(form.Value, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"");
        Assert.True(token.Success);
        using var response = await client.PostAsync("/?handler=SetLanguage&returnUrl=" + Uri.EscapeDataString(returnUrl),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"] = culture,
                ["__RequestVerificationToken"] = token.Groups["value"].Value
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")
, value => value.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal));
        Assert.Contains(cookieValue, cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient Client(string origin) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(origin),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private sealed class UnusedSubmissionService : IInstantQuotationSubmissionService
    {
        public Task<InstantQuotationSubmissionResult> SubmitAsync(string sessionId, string? ownerIdentity,
            InstantQuotationCustomerSubmission customer, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A legacy JSON read must not submit a quotation.");
    }

    private static HttpRequestMessage Request(string method, string path, string? cookie)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static void AssertDocumentLinks(string document, string canonical)
    {
        Assert.Single(Regex.Matches(document, "<link(?=[^>]*rel=\"canonical\")(?=[^>]*href=\""
            + Regex.Escape(canonical) + "\")[^>]*>"));
        foreach (var (language, url) in new[]
        {
            ("en", "https://www.maliev.com/about?culture=en"),
            ("th", "https://www.maliev.com/about"),
            ("x-default", "https://www.maliev.com/about")
        })
        {
            Assert.Single(Regex.Matches(document, "<link(?=[^>]*rel=\"alternate\")(?=[^>]*hreflang=\""
                + Regex.Escape(language) + "\")(?=[^>]*href=\"" + Regex.Escape(url) + "\")[^>]*>"));
        }
    }

    private static string DocumentLinks(string document) => string.Join("\n",
        Regex.Matches(document, "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>")
            .Select(match => match.Value));
}
