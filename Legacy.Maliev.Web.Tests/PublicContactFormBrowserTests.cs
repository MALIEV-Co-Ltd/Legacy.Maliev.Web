using System.Net;
using System.Net.Sockets;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class PublicContactFormBrowserTests(PublicContactBrowserFixture fixture)
{
    [Fact]
    public async Task ContactForm_RejectsOptionalCookiesAndPostsOnlyToItsRenderedAction()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, "/contact?culture=en").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Contact returned HTTP {response.Status}.");

        var consent = page.Locator("#cookieConsent");
        await consent.Locator("[data-consent-action='reject']").ClickAsync();
        Assert.Equal(0, await consent.CountAsync());
        Assert.Contains("maliev_tracking_consent=denied", (await context.CookiesAsync())
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));

        await page.Locator("#FirstName").FillAsync("Browser");
        await page.Locator("#LastName").FillAsync("Submission");
        await page.Locator("#Email").FillAsync("browser-submission@example.test");
        await page.Locator("#Message").FillAsync("Please quote a manufacturing project.");
        await page.Locator("#Country").SelectOptionAsync("Thailand");

        var request = await CaptureFormPostAsync(page, "#contact-us");
        var posted = request.PostData ?? string.Empty;
        Assert.Equal("POST", request.Method);
        Assert.Equal("document", request.ResourceType);
        Assert.Contains("handler=SubmitRequest", request.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__RequestVerificationToken=", posted, StringComparison.Ordinal);
        Assert.Contains("FirstName=Browser", posted, StringComparison.Ordinal);
        Assert.Contains("Country=Thailand", posted, StringComparison.Ordinal);
        Assert.Contains("browser-submission%40example.test", posted, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IRequest> CaptureFormPostAsync(IPage page, string formSelector)
    {
        var action = new Uri(await page.Locator(formSelector).EvaluateAsync<string>("form => form.action"));
        var submitted = new TaskCompletionSource<IRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            if (request.Method == "POST"
                && request.ResourceType == "document"
                && new Uri(request.Url).Equals(action))
            {
                submitted.TrySetResult(request);
                await route.AbortAsync();
                return;
            }

            await route.ContinueAsync();
        });

        var invalidFields = await page.Locator(formSelector).EvaluateAsync<string[]>(
            "form => Array.from(form.elements).filter(field => !field.checkValidity()).map(field => field.name || field.id)");
        Assert.True(invalidFields.Length == 0,
            $"{formSelector} did not pass browser validation: {string.Join(", ", invalidFields)}.");

        await page.Locator(formSelector).EvaluateAsync("form => form.submit()");
        return await submitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

public sealed class PublicContactBrowserFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? factory;
    private IPlaywright? playwright;

    public IBrowser Browser { get; private set; } = null!;
    public Uri Origin { get; private set; } = null!;

    /// <summary>Resolves the production typed localizer from this collection's owned host.</summary>
    public IStringLocalizer<ThreeDimensionalPrintingEstimateContent> PreliminaryQuotationLocalizer =>
        (factory ?? throw new InvalidOperationException("The owned host must be initialized."))
            .Services.GetRequiredService<IStringLocalizer<ThreeDimensionalPrintingEstimateContent>>();

    public async Task InitializeAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Origin = new Uri($"http://127.0.0.1:{port}");

        factory = new PublicContactTestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = Origin,
        });
        using var response = await client.GetAsync("/contact?culture=en");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        playwright?.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }
    }

    private sealed class PublicContactTestingWebApplicationFactory(string contentRoot)
        : TestingWebApplicationFactory(contentRoot)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        }
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)], true));
    }
}

[CollectionDefinition(Name)]
public sealed class PublicContactBrowserCollection : ICollectionFixture<PublicContactBrowserFixture>
{
    public const string Name = "Public contact browser";
}
