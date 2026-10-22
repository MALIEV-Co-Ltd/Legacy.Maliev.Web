using System.Net;
using System.Net.Sockets;
using System.Text;
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
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "th-TH",
            UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 LINE/15.10.0",
        });
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
        Assert.Contains("Message=Please+quote+a+manufacturing+project.", posted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/quotation/cnc-machining?culture=th", "#quotation-form", "SubmitRequest", "FirstName,LastName,Email,Phone,Company,TaxNumber,Country,Message", true)]
    [InlineData("/account/login?culture=th&returnUrl=%2FMember%2FOrders", "#customer-login", "Login", "Email,Password,ReturnUrl", false)]
    [InlineData("/account/signup?culture=th", "#customer-signup", "SignUp", "FirstName,LastName,Email,Password,ConfirmPassword", false)]
    [InlineData("/account/forgotpassword?culture=th", ".auth-card form", "PasswordReset", "Email", false)]
    [InlineData("/account/resetpassword?culture=th&email=browser-submission%40example.test&token=abcdefghijklmnopqrstuvwxyz123456", ".auth-card form", "ChangePassword", "Email,Token,Password,ConfirmPassword", false)]
    public async Task OriginalPublicForm_NativeBrowserPostPreservesFieldsAndRenderedSubmitter(
        string path, string formSelector, string handler, string requiredFields, bool includesFile)
    {
        // Source parity is native validation/serialization, not reCAPTCHA or JavaScript enhancement acceptance.
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "th-TH",
            UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 LINE/15.10.0",
            JavaScriptEnabled = false,
        });
        await context.AddCookiesAsync([new Microsoft.Playwright.Cookie
        {
            Name = "maliev_tracking_consent",
            Value = "denied",
            Url = fixture.Origin.AbsoluteUri,
        }]);
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, path).ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, "The original public form route must render successfully.");
        Assert.Contains("maliev_tracking_consent=denied", (await context.CookiesAsync())
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));

        await FillIfPresentAsync(page, "#FirstName", "Browser");
        await FillIfPresentAsync(page, "#LastName", "Submission");
        await FillIfPresentAsync(page, "#Email", "browser-submission@example.test");
        await FillIfPresentAsync(page, "#Password", "FormTest123!");
        await FillIfPresentAsync(page, "#ConfirmPassword", "FormTest123!");
        await FillIfPresentAsync(page, "#Phone", "0818030404");
        await FillIfPresentAsync(page, "#Company", "MALIEV Form Test");
        await FillIfPresentAsync(page, "#TaxNumber", "0125561001573");
        await FillIfPresentAsync(page, "#Message", "CNC machining; aluminium 6061; quantity 2; drawing attached.");
        if (includesFile)
        {
            await page.Locator("#Country").SelectOptionAsync("Thailand");
            await page.SetInputFilesAsync("#Files", new FilePayload
            {
                Name = "cnc-bracket.step",
                MimeType = string.Empty,
                Buffer = Encoding.ASCII.GetBytes("ISO-10303-21; END-ISO-10303-21;"),
            });
            // Playwright infers binary for an empty FilePayload MIME type and an unknown extension.
            Assert.Equal("application/octet-stream", await page.Locator("#Files")
                .EvaluateAsync<string>("input => input.files[0].type"));
        }

        var submitter = page.Locator(formSelector).Locator("button[type='submit']").First;
        var action = new Uri(await submitter.EvaluateAsync<string>("button => button.hasAttribute('formaction') ? button.formAction : button.form.action"));
        Assert.Equal(fixture.Origin.GetLeftPart(UriPartial.Authority), action.GetLeftPart(UriPartial.Authority));
        Assert.Equal(handler, Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(action.Query)["handler"].ToString());

        var request = await CaptureFormPostAsync(page, formSelector, "button[type='submit']");
        Assert.Equal("POST", request.Method);
        Assert.Equal("document", request.ResourceType);
        Assert.Equal(action, new Uri(request.Url));
        Assert.NotNull(request.PostDataBuffer);
        var posted = includesFile
            ? Encoding.Latin1.GetString(request.PostDataBuffer!)
            : request.PostData ?? string.Empty;
        Assert.Contains("__RequestVerificationToken", posted, StringComparison.Ordinal);
        foreach (var field in requiredFields.Split(','))
        {
            Assert.Contains(includesFile ? $"name=\"{field}\"" : $"{field}=", posted, StringComparison.Ordinal);
        }
        Assert.True(posted.Contains("browser-submission@example.test", StringComparison.OrdinalIgnoreCase)
            || posted.Contains("browser-submission%40example.test", StringComparison.OrdinalIgnoreCase));
        if (includesFile)
        {
            Assert.Contains("multipart/form-data", request.Headers["content-type"], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Content-Type: application/octet-stream", posted, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cnc-bracket.step", posted, StringComparison.Ordinal);
            Assert.Contains("CNC machining; aluminium 6061; quantity 2; drawing attached.", posted, StringComparison.Ordinal);
        }
    }

    private static async Task FillIfPresentAsync(IPage page, string selector, string value)
    {
        var input = page.Locator(selector);
        if (await input.CountAsync() > 0 && await input.IsVisibleAsync())
        {
            await input.FillAsync(value);
        }
    }

    private static async Task<IRequest> CaptureFormPostAsync(IPage page, string formSelector, string? submitterSelector = null)
    {
        var form = page.Locator(formSelector);
        var submitter = submitterSelector is null ? null : form.Locator(submitterSelector).First;
        var action = new Uri(submitter is null
            ? await form.EvaluateAsync<string>("form => form.action")
            : await submitter.EvaluateAsync<string>("button => button.hasAttribute('formaction') ? button.formAction : button.form.action"));
        var submitted = new TaskCompletionSource<IRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            if (request.Method == "POST" && request.ResourceType == "document")
            {
                if (new Uri(request.Url).Equals(action))
                {
                    submitted.TrySetResult(request);
                }
                else
                {
                    submitted.TrySetException(new InvalidOperationException("The form posted to an unexpected document action."));
                }
                await route.AbortAsync();
                return;
            }

            await route.ContinueAsync();
        });

        var invalidFields = await page.Locator(formSelector).EvaluateAsync<string[]>(
            "form => Array.from(form.elements).filter(field => !field.checkValidity()).map(field => field.name || field.id)");
        Assert.True(invalidFields.Length == 0,
            $"{formSelector} did not pass browser validation: {string.Join(", ", invalidFields)}.");

        if (submitter is null)
        {
            await form.EvaluateAsync("form => form.submit()");
        }
        else
        {
            await submitter.EvaluateAsync("button => button.form.requestSubmit(button)");
        }
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

    public async Task InitializeAsync()
    {
        try
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
