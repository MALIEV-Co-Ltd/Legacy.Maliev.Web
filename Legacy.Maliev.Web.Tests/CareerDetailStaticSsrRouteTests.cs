using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CareerDetailStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public CareerDetailStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
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
        Assert.Contains("<meta property=\"og:image\" content=\"https://www.maliev.com/src/images/career-3dprinting.jpg\"", source, StringComparison.Ordinal);
        Assert.Contains($">{status}<", source, StringComparison.Ordinal);
        Assert.Contains($">{responsibilities}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:career@maliev.com\"", source, StringComparison.Ordinal);
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

    private WebApplicationFactory<Program> CreateFactory(
        ICareerClient careerClient,
        bool careerRouteEnabled = true) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:CareerDetail", careerRouteEnabled.ToString());
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
    }

    private sealed class CareerClientStub(CareerResponseMode mode) : ICareerClient
    {
        private static readonly CareerLevel Level = new(1, "Engineer", null, null, null);
        private static readonly CareerOffer Offer = new(
            7,
            Level.Id,
            "Manufacturing Engineer",
            null,
            "Build reliable manufacturing processes.",
            "Engineering experience.",
            "A practical team.",
            "Nonthaburi",
            false,
            null,
            null,
            Level);

        public List<int> RequestedOfferIds { get; } = [];

        public static CareerClientStub Success() => new(CareerResponseMode.Success);

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
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
        }
    }
}
