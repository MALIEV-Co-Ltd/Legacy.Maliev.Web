using System.Net;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CareerIndexStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public CareerIndexStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheCareerRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Career", "CareerIndexPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Career", "CareerIndexContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Career", "Index.cshtml"));

        Assert.Contains("BlazorRouting:CareerIndex", program, StringComparison.Ordinal);
        Assert.Contains("\"/Career/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("\"CareerIndex\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(CareerIndexContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Career | MALIEV",
        "Discover your perfect job match, and the freedom and support to take your career to the next level.",
        "Job Offers")]
    [InlineData(
        "th",
        "ตำแหน่งงาน | MALIEV",
        "เข้ามาร่วมงานกับเรา เพื่ออิสระในการทำงานและการสนับสนุนที่จะผลักดันอาชีพคุณไปอีกระดับ",
        "ตำแหน่งงาน")]
    public async Task CareerRoute_RendersLocalizedServiceBackedStaticSsrWithSeoAndAnalytics(
        string culture,
        string title,
        string description,
        string heading)
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub);
        using var client = CreateClient(routeFactory);
        using var response = await client.GetAsync(
            $"/career?culture={culture}&sort=JobId_Ascending&search=%20engineer%20&index=3&size=50&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.Contains("Manufacturing Engineer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Filled role", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wowjs", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(new CareerRequest(CareerSort.JobId_Ascending, "engineer", 1, 50), clientStub.LastRequest);
    }

    [Fact]
    public async Task CareerRoute_ClampsInvalidPagingAndPreservesCanonicalDocumentLinks()
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub);
        using var client = CreateClient(routeFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync(
            "/career?culture=en&sort=not-a-sort&index=-4&size=500&tracking=excluded"));

        Assert.Equal(new CareerRequest(CareerSort.JobCreatedDate_Descending, null, 1, 100), clientStub.LastRequest);
        Assert.Equal(1, CountLink(source, "canonical", "https://www.maliev.com/career?culture=en"));
        Assert.Equal(1, CountAlternate(source, "en", "https://www.maliev.com/career?culture=en"));
        Assert.Equal(1, CountAlternate(source, "th", "https://www.maliev.com/career"));
        Assert.Equal(1, CountAlternate(source, "x-default", "https://www.maliev.com/career"));
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledCareerRoute_UsesTheRetainedRazorFallback()
    {
        var clientStub = new CapturingCareerClient();
        await using var routeFactory = CreateFactory(clientStub, careerRouteEnabled: false);
        using var client = CreateClient(routeFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/career?culture=en"));

        Assert.Contains("<title>Career | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateFactory(
        ICareerClient careerClient,
        bool careerRouteEnabled = true) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:CareerIndex", careerRouteEnabled.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICareerClient>();
                services.AddSingleton(careerClient);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) => sourceFactory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static string ExtractDocumentLinks(string source) => string.Join(
        Environment.NewLine,
        System.Text.RegularExpressions.Regex.Matches(
                source,
                "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Select(match => match.Value));

    private static int CountLink(string source, string relation, string url) =>
        System.Text.RegularExpressions.Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"{System.Text.RegularExpressions.Regex.Escape(relation)}\")(?=[^>]*href=\"{System.Text.RegularExpressions.Regex.Escape(url)}\")[^>]*>",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count;

    private static int CountAlternate(string source, string language, string url) =>
        System.Text.RegularExpressions.Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"alternate\")(?=[^>]*href=\"{System.Text.RegularExpressions.Regex.Escape(url)}\")(?=[^>]*hreflang=\"{System.Text.RegularExpressions.Regex.Escape(language)}\")[^>]*>",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class CapturingCareerClient : ICareerClient
    {
        private static readonly CareerLevel Level = new(1, "Engineer", null, null, null);

        public CareerRequest? LastRequest { get; private set; }

        public Task<CareerListing> GetListingAsync(
            CareerSort sort,
            string? search,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken)
        {
            LastRequest = new CareerRequest(sort, search, pageIndex, pageSize);
            return Task.FromResult(new CareerListing(
                [Level],
                new CareerOfferPage(
                    [
                        new CareerOffer(1, 1, "Manufacturing Engineer", null, null, null, null, "Nonthaburi", false, null, null, Level),
                        new CareerOffer(2, 1, "Filled role", null, null, null, null, "Bangkok", true, null, null, Level),
                    ],
                    pageIndex,
                    3,
                    6,
                    pageIndex > 1,
                    pageIndex < 3),
                true));
        }

        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<CareerOffer>(null, true));
    }

    private sealed record CareerRequest(CareerSort Sort, string? Search, int PageIndex, int PageSize);
}
