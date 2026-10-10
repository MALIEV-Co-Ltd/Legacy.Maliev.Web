using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CareerDetailStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public CareerDetailStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresTheCareerDetailRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Career", "CareerDetailPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var route = File.ReadAllText(routePath);
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Career", "CareerDetailContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Career", "View.cshtml"));

        Assert.Contains("BlazorRouting:CareerDetail", program, StringComparison.Ordinal);
        Assert.Contains("\"/Career/View\"", program, StringComparison.Ordinal);
        Assert.Contains("\"CareerDetail\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("@attribute [StreamRendering(false)]", route, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(CareerDetailContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Discover your perfect job match, and the freedom and support to take your career to the next level.",
        "Urgent",
        "Responsibilities")]
    [InlineData(
        "th",
        "เข้ามาร่วมงานกับเรา เพื่ออิสระในการทำงานและการสนับสนุนที่จะผลักดันอาชีพคุณไปอีกระดับ",
        "เร่งด่วน",
        "หน้าที่ความรับผิดชอบ")]
    public async Task CareerDetailRoute_RendersLocalizedServiceBackedStaticSsrWithSeoAndAnalytics(
        string culture,
        string description,
        string status,
        string responsibilities)
    {
        var careerClient = CareerClientStub.Success();
        await using var routeFactory = CreateFactory(careerClient);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career/view/7?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([7], careerClient.RequestedOfferIds);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains("<title>Manufacturing Engineer | Career@MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:title\" content=\"Manufacturing Engineer | Career@MALIEV\"", source, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:image\" content=\"https://www.maliev.com/src/images/career-3dprinting.webp\"", source, StringComparison.Ordinal);
        Assert.Contains($">{status}<", source, StringComparison.Ordinal);
        Assert.Contains($">{responsibilities}<", source, StringComparison.Ordinal);
        Assert.Contains("Build reliable manufacturing processes.", source, StringComparison.Ordinal);
        Assert.Contains("Own release validation.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("career-xss", source, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(1)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("fb-like", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connect.facebook.net", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/contact#contact-us\" data-contact-email=\"career@maliev.com\"", source, StringComparison.Ordinal);
        Assert.Contains("onclick=\"PrintJobDescription()\"", source, StringComparison.Ordinal);
        Assert.Contains("function PrintJobDescription()", source, StringComparison.Ordinal);
        Assert.Equal(1, CountLink(source, "canonical", culture == "en"
            ? "https://www.maliev.com/career/view/7?culture=en"
            : "https://www.maliev.com/career/view/7"));
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wowjs", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/career/view/not-a-number", CareerResponseMode.Success, HttpStatusCode.BadRequest, 0)]
    [InlineData("/career/view/0", CareerResponseMode.Success, HttpStatusCode.BadRequest, 0)]
    [InlineData("/career/view/999", CareerResponseMode.Missing, HttpStatusCode.NotFound, 1)]
    [InlineData("/career/view/7", CareerResponseMode.Unavailable, HttpStatusCode.ServiceUnavailable, 1)]
    public async Task CareerDetailRoute_PreservesSafeFailureStatusContracts(
        string route,
        CareerResponseMode mode,
        HttpStatusCode expectedStatus,
        int expectedRequests)
    {
        var careerClient = new CareerClientStub(mode);
        await using var routeFactory = CreateFactory(careerClient);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"{route}?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedRequests, careerClient.RequestedOfferIds.Count);
        Assert.Contains("data-migration-component=\"error-content\"", source, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,nofollow\"", source, StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.DoesNotContain("sensitive exception detail", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CareerDetailRoute_UnexpectedServiceFailureReturnsSafeGeneric500()
    {
        var careerClient = new CareerClientStub(CareerResponseMode.Throw);
        await using var routeFactory = CreateFactory(careerClient);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync("/career/view/7?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Something did not work properly", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"error-content\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive exception detail", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CareerDetailRoute_WithUnknownFilledState_DoesNotClaimUrgentOrFilled()
    {
        var careerClient = new CareerClientStub(CareerResponseMode.UnknownFilledState);
        await using var routeFactory = CreateFactory(careerClient);
        using var client = CreateClient(routeFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/career/view/7?culture=en"));

        Assert.DoesNotContain(">Urgent<", source, StringComparison.Ordinal);
        Assert.DoesNotContain(">Filled<", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalReviewCareerDetail_HidesTheExactAspireFixture()
    {
        var careerClient = new CareerClientStub(CareerResponseMode.LocalAspireFixture);
        await using var routeFactory = CreateFactory(careerClient, hideLocalAspireFixture: true);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync("/career/view/7?culture=en");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DisabledCareerDetailRoute_UsesTheRetainedRazorFallback()
    {
        var careerClient = CareerClientStub.Success();
        await using var routeFactory = CreateFactory(careerClient, careerRouteEnabled: false);
        using var client = CreateClient(routeFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/career/view/7?culture=en"));

        Assert.Contains("<title>Manufacturing Engineer | Career@MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> OriginalDetailLevelCases()
    {
        foreach (var renderer in new[] { true, false })
        {
            foreach (var culture in new[] { "en", "th" })
            {
                foreach (var mode in new[] { "omitted", "conflicting", "missing", "null-name", "levels404", "levels503" })
                {
                    yield return [renderer, culture, mode];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(OriginalDetailLevelCases))]
    public async Task Detail_ActualTypedClient_ResolvesOriginalSeparateLevelLookup(
        bool rendererEnabled, string culture, string mode)
    {
        using var handler = new DetailSourceHandler(mode);
        using var upstream = new HttpClient(handler) { BaseAddress = new Uri("http://career-detail-source/") };
        var typed = new CareerClient(new DetailSourceFactory(upstream), NullLogger<CareerClient>.Instance);
        await using var routeFactory = CreateFactory(typed, careerRouteEnabled: rendererEnabled);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync($"/career/view/7?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Contains("Source detail offer", html, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/Jobs/7");
        Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/jobs/levels");
        Assert.DoesNotContain("Nested conflicting name", html, StringComparison.Ordinal);
        if (mode is "omitted" or "conflicting")
        {
            Assert.Contains("Independent source level", html, StringComparison.Ordinal);
            Assert.DoesNotContain("not specified", html, StringComparison.Ordinal);
        }
        else if (mode == "null-name")
        {
            Assert.DoesNotContain("Independent source level", html, StringComparison.Ordinal);
            Assert.DoesNotContain("not specified", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("not specified", html, StringComparison.Ordinal);
        }
    }

    private sealed class DetailSourceFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("careers", name);
            return client;
        }
    }

    private sealed class DetailSourceHandler(string mode) : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentQueue<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing detail URI.");
            Requests.Enqueue(uri);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.True(uri.AbsolutePath is "/Jobs/7" or "/jobs/levels");
            var status = HttpStatusCode.OK;
            string json;
            if (uri.AbsolutePath == "/Jobs/7")
            {
                var nested = mode == "conflicting"
                    ? ",\"level\":{\"id\":1,\"name\":\"Nested conflicting name\"}"
                    : string.Empty;
                json = "{\"id\":7,\"levelId\":1,\"title\":\"Source detail offer\",\"isFilled\":false" + nested + "}";
            }
            else
            {
                status = mode == "levels404" ? HttpStatusCode.NotFound
                    : mode == "levels503" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
                json = mode is "missing" or "levels404" or "levels503" ? "[]"
                    : mode == "null-name" ? "[{\"id\":1,\"name\":null}]"
                    : "[{\"id\":1,\"name\":\"Independent source level\"}]";
            }

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private WebApplicationFactory<Program> CreateFactory(
        ICareerClient careerClient,
        bool careerRouteEnabled = true,
        bool hideLocalAspireFixture = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:CareerDetail", careerRouteEnabled.ToString());
            builder.UseSetting("Career:HideLocalAspireFixture", hideLocalAspireFixture.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICareerClient>();
                services.AddSingleton(careerClient);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) => sourceFactory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static int CountLink(string source, string relation, string url) =>
        Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"{Regex.Escape(relation)}\")(?=[^>]*href=\"{Regex.Escape(url)}\")[^>]*>",
            RegexOptions.CultureInvariant).Count;

    private static string ExtractDocumentLinks(string source) => string.Join(
        Environment.NewLine,
        Regex.Matches(source, "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>", RegexOptions.CultureInvariant)
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

    public enum CareerResponseMode
    {
        Success,
        Missing,
        Unavailable,
        Throw,
        UnknownFilledState,
        LocalAspireFixture,
    }

    private sealed class CareerClientStub(CareerResponseMode mode) : ICareerClient
    {
        private static readonly CareerLevel Level = new(1, "Engineer", null, null, null);
        private static readonly CareerOffer Offer = new(
            7,
            Level.Id,
            "Manufacturing Engineer",
            null,
            "<p>Build <strong>reliable</strong> manufacturing processes.</p><script id=\"career-xss\">alert(1)</script>",
            "Engineering experience.",
            "<ul><li>A practical team.</li><li>Own release validation.</li></ul>",
            "Nonthaburi",
            false,
            null,
            null,
            Level);

        public List<int> RequestedOfferIds { get; } = [];

        public static CareerClientStub Success() => new(CareerResponseMode.Success);

        public Task<ServiceResponse<IReadOnlyList<CareerLevel>>> GetLevelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<CareerLevel>>([Level], true));

        public async Task<ServiceResponse<CareerOfferPage>> GetOffersAsync(
            CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            var listing = await GetListingAsync(sort, search, pageIndex, pageSize, cancellationToken);
            return new ServiceResponse<CareerOfferPage>(listing.Offers, listing.ServiceAvailable);
        }

        public Task<CareerListing> GetListingAsync(
            CareerSort sort,
            string? search,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CareerListing([], CareerOfferPage.Empty(pageIndex), true));

        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(
            int offerId,
            CancellationToken cancellationToken)
        {
            RequestedOfferIds.Add(offerId);
            return mode switch
            {
                CareerResponseMode.Success => Task.FromResult(new ServiceResponse<CareerOffer>(Offer, true)),
                CareerResponseMode.Missing => Task.FromResult(new ServiceResponse<CareerOffer>(null, true)),
                CareerResponseMode.Unavailable => Task.FromResult(new ServiceResponse<CareerOffer>(null, false)),
                CareerResponseMode.Throw => Task.FromException<ServiceResponse<CareerOffer>>(
                    new InvalidOperationException("sensitive exception detail")),
                CareerResponseMode.UnknownFilledState => Task.FromResult(
                    new ServiceResponse<CareerOffer>(Offer with { IsFilled = null }, true)),
                CareerResponseMode.LocalAspireFixture => Task.FromResult(
                    new ServiceResponse<CareerOffer>(
                        new CareerOffer(
                            7,
                            1,
                            "Local Manufacturing Engineer",
                            "Local Aspire career boundary verification",
                            "Support digital manufacturing projects.",
                            "Manufacturing experience",
                            "Independent engineering work",
                            "Nonthaburi",
                            false,
                            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                            null,
                            new CareerLevel(
                                1,
                                "Experienced",
                                "Local Aspire verification level",
                                new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                                null)),
                        true)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
        }
    }
}
