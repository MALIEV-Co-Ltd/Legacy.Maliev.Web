using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
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

    [Theory]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    [InlineData("en", 390)]
    [InlineData("th", 390)]
    public async Task Footer_PreferredSourceLink_IsLocalizedAndAccessible(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 1000 },
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, "/legal?culture=" + culture).ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Footer page returned HTTP {response.Status}.");
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var link = page.Locator(".landing-preferred-source");
        await Assertions.Expect(link).ToHaveCountAsync(1);
        await Assertions.Expect(link).ToHaveAttributeAsync("href", "https://www.google.com/preferences/source?q=maliev.com");
        await Assertions.Expect(link).ToHaveAttributeAsync("target", "_blank");
        await Assertions.Expect(link).ToHaveAttributeAsync("rel", "noopener noreferrer");
        await Assertions.Expect(link).ToHaveTextAsync(culture == "th"
            ? "เพิ่ม MALIEV เป็นแหล่งข้อมูลที่ต้องการบน Google"
            : "Add MALIEV as a preferred source on Google");
        await link.FocusAsync();
        await Assertions.Expect(link).ToBeFocusedAsync();
        Assert.True(await link.EvaluateAsync<bool>(@"el => {
            const box = el.getBoundingClientRect();
            return box.width >= 44 && box.height >= 44 && el.scrollWidth <= el.clientWidth + 1
                && box.left >= 0 && box.right <= innerWidth;
        }"));
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
    private PublicContactRenderedScripts? renderedScripts;

    public IBrowser Browser { get; private set; } = null!;
    public Uri Origin { get; private set; } = null!;

    /// <summary>Resolves the production typed localizer from this collection's owned host.</summary>
    public IStringLocalizer<ThreeDimensionalPrintingEstimateContent> PreliminaryQuotationLocalizer =>
        (factory ?? throw new InvalidOperationException("The owned host must be initialized."))
            .Services.GetRequiredService<IStringLocalizer<ThreeDimensionalPrintingEstimateContent>>();

    /// <summary>Returns the immutable script captured from the owned public host for this culture.</summary>
    internal string GetRenderedContactScript(string culture) =>
        (renderedScripts ?? throw new InvalidOperationException("The owned contact scripts must be captured first."))
            .Get(culture);

    private static int ReserveFreePort()
    {
        // Keep test listeners outside the outbound client-port pool on Windows.
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, Random.Shared.Next(20000, 30000));
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                // Another task or an excluded port range owns this candidate.
            }
            finally
            {
                listener.Stop();
            }
        }

        throw new InvalidOperationException("No loopback browser-test port is available in the test-server range.");
    }

    public async Task InitializeAsync()
    {
        try
        {
            var port = ReserveFreePort();
            Origin = new Uri($"http://127.0.0.1:{port}");

            factory = new PublicContactTestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
            factory.UseKestrel(port);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = Origin,
            });
            renderedScripts = await PublicContactRenderedScripts.CaptureAsync(client);

            playwright = await Playwright.CreateAsync();
            Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (Exception initializationFailure)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Contact fixture initialization and cleanup failed.",
                    initializationFailure, cleanupFailure);
            }

            throw;
        }
    }

    public async Task DisposeAsync()
    {
        var ownedBrowser = Browser;
        var ownedPlaywright = playwright;
        var ownedFactory = factory;
        Browser = null!;
        playwright = null;
        factory = null;
        renderedScripts = null;

        try
        {
            if (ownedBrowser is not null)
            {
                await ownedBrowser.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                ownedPlaywright?.Dispose();
            }
            finally
            {
                if (ownedFactory is not null)
                {
                    await ownedFactory.DisposeAsync();
                }
            }
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

internal sealed class PublicContactRenderedScripts
{
    private readonly string english;
    private readonly string thai;

    private PublicContactRenderedScripts(string english, string thai)
    {
        this.english = english;
        this.thai = thai;
    }

    internal string Get(string culture) => culture switch
    {
        "en" => english,
        "th" => thai,
        _ => throw new ArgumentOutOfRangeException(nameof(culture), culture, "Only captured public cultures are supported.")
    };

    internal static async Task<PublicContactRenderedScripts> CaptureAsync(HttpClient client)
    {
        var englishScript = await CaptureCultureAsync(client, "en");
        var thaiScript = await CaptureCultureAsync(client, "th");
        return new PublicContactRenderedScripts(englishScript, thaiScript);
    }

    private static async Task<string> CaptureCultureAsync(HttpClient client, string culture)
    {
        // Match the existing handler-request ceiling; never retry or extend it.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync($"/contact?culture={culture}",
            HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidDataException($"The {culture} public contact capture did not return HTTP 200.");
        }

        var html = await response.Content.ReadAsStringAsync(deadline.Token);
        if (!Regex.IsMatch(html, $"<html\\b[^>]*\\slang=\"{culture}\"(?:\\s|>)", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException($"The public contact capture did not render culture {culture}.");
        }

        var matches = Regex.Matches(html,
            "<script[^>]*data-migration-component=\"public-contact-channel-analytics\"[^>]*>(?<script>[\\s\\S]*?)</script>",
            RegexOptions.CultureInvariant);
        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0].Groups["script"].Value))
        {
            throw new InvalidDataException($"The {culture} public contact capture must contain one nonblank handler.");
        }

        return matches[0].Groups["script"].Value;
    }
}

[CollectionDefinition(Name)]
public sealed class PublicContactBrowserCollection : ICollectionFixture<PublicContactBrowserFixture>
{
    public const string Name = "Public contact browser";
}
