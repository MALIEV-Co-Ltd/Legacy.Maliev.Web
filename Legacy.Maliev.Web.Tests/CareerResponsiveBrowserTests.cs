using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks the source Career listing layout through the rendered page.</summary>
public sealed class CareerResponsiveBrowserTests(CareerResponsiveBrowserFixture fixture)
    : IClassFixture<CareerResponsiveBrowserFixture>
{
    /// <summary>Checks localized mobile stacking and desktop table layout with a synthetic offer.</summary>
    /// <param name="culture">Requested page language.</param>
    /// <param name="width">Viewport width in CSS pixels.</param>
    /// <returns>A task representing the browser check.</returns>
    [Theory]
    [InlineData("en", 390)]
    [InlineData("th", 390)]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    public async Task CareerListing_UsesLocalizedResponsiveTable(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 844 },
        });
        await context.RouteAsync("**/*", route =>
            new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) == fixture.Origin.GetLeftPart(UriPartial.Authority)
                ? route.ContinueAsync()
                : route.AbortAsync());
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/career?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var region = page.Locator(".career-table-wrapper");
        var row = page.Locator("#search-result tbody tr").First;
        var cells = row.Locator("td");
        Assert.True(await region.IsVisibleAsync());
        Assert.Equal("region", await region.GetAttributeAsync("role"));
        Assert.False(string.IsNullOrWhiteSpace(await region.GetAttributeAsync("aria-label")));
        Assert.Equal(4, await cells.CountAsync());
        Assert.Contains("Synthetic engineer", await row.InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(width < 768 ? "block" : "table-row", await row.EvaluateAsync<string>("element => getComputedStyle(element).display"));
        Assert.Equal(width < 768 ? "grid" : "table-cell", await cells.Nth(0).EvaluateAsync<string>("element => getComputedStyle(element).display"));
        if (width < 768)
        {
            for (var index = 0; index < 4; index++)
            {
                var cell = cells.Nth(index);
                var label = await cell.GetAttributeAsync("data-title");
                Assert.False(string.IsNullOrWhiteSpace(label));
                Assert.Equal($"\"{label}\"", await cell.EvaluateAsync<string>("element => getComputedStyle(element, '::before').content"));
            }
        }

        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        await region.FocusAsync();
        Assert.True(await region.EvaluateAsync<bool>("element => document.activeElement === element"));
        var screenshots = Path.Combine(AppContext.BaseDirectory, "career-responsive-screenshots");
        Directory.CreateDirectory(screenshots);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(screenshots, $"career-{culture}-{width}.png"), FullPage = true });
    }
}

/// <summary>Owns a loopback-only host with a synthetic Career client and one browser.</summary>
public sealed class CareerResponsiveBrowserFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? factory;
    private IPlaywright? playwright;

    /// <summary>Gets the owned browser.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>Gets the loopback host origin.</summary>
    public Uri Origin { get; private set; } = null!;

    /// <summary>Starts the synthetic host and browser.</summary>
    /// <returns>A task representing setup.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            var port = BrowserTestServerPort.Reserve();
            Origin = new Uri($"http://127.0.0.1:{port}");
            factory = new CareerResponsiveWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
            factory.UseKestrel(port);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = Origin });
            using var ready = await client.GetAsync("/career?culture=en");
            Assert.Equal(System.Net.HttpStatusCode.OK, ready.StatusCode);
            Assert.Contains("Synthetic engineer", await ready.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            var siteCss = Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot", "dist", "site.min.css");
            Assert.Equal(await File.ReadAllBytesAsync(siteCss), await client.GetByteArrayAsync("/dist/site.min.css"));
            playwright = await Playwright.CreateAsync();
            Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (Exception initializationFailure)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanupFailure) { throw new AggregateException("Career fixture initialization and cleanup failed.", initializationFailure, cleanupFailure); }
            throw;
        }
    }

    /// <summary>Releases the owned browser and host.</summary>
    /// <returns>A task representing cleanup.</returns>
    public async Task DisposeAsync()
    {
        var ownedBrowser = Browser;
        var ownedPlaywright = playwright;
        var ownedFactory = factory;
        Browser = null!;
        playwright = null;
        factory = null;
        try { if (ownedBrowser is not null) { await ownedBrowser.DisposeAsync(); } }
        finally
        {
            try { ownedPlaywright?.Dispose(); }
            finally { if (ownedFactory is not null) { await ownedFactory.DisposeAsync(); } }
        }
    }

    private sealed class CareerResponsiveWebApplicationFactory(string contentRoot) : TestingWebApplicationFactory(contentRoot)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICareerClient>();
                services.AddSingleton<ICareerClient, SyntheticCareerClient>();
            });
        }
    }

    private sealed class SyntheticCareerClient : ICareerClient
    {
        private static readonly CareerLevel Level = new(1, "Engineering", null, null, null);
        private static readonly CareerOffer Offer = new(1, 1, "Synthetic engineer", null, null, null, null, "Bangkok", false, null, null, Level);
        private static readonly CareerOfferPage Page = new([Offer], 1, 1, 1, false, false);

        public Task<CareerListing> GetListingAsync(CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new CareerListing([Level], Page, true));
        public Task<ServiceResponse<CareerOfferPage>> GetOffersAsync(CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<CareerOfferPage>(Page, true));
        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<CareerOffer>(Offer, true));
        public Task<ServiceResponse<IReadOnlyList<CareerLevel>>> GetLevelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<CareerLevel>>([Level], true));
    }
}
