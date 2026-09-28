using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class MemberOrderUploadBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", "Optional. Maximum total upload size: 100 MB.")]
    [InlineData("th", "ไม่บังคับ ขนาดรวมสูงสุด 100 MB")]
    public async Task AuthenticatedMemberForm_RendersLocalizedUploadCap(string culture, string helpText)
    {
        await using var host = new MemberOrderBrowserHost();
        await host.StartAsync();
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = BrowserAuthenticationHandler.SchemeName },
        });
        await using var page = await context.NewPageAsync();
        page.SetDefaultTimeout(120_000);

        var response = await page.GotoAsync($"{host.Origin}/member/orders/3d-scanning?culture={culture}");
        Assert.Equal(200, response?.Status);
        Assert.Contains("/member/orders/3d-scanning", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await page.Locator("[data-member-order-form] input[type=file][name=Files]").CountAsync());
        Assert.Equal(1, await page.GetByText(helpText).CountAsync());
        Assert.Equal(0, await page.GetByText("200 MB", new() { Exact = false }).CountAsync());
    }

    [Theory]
    [InlineData("en", "The total upload size cannot exceed 100 MB.")]
    [InlineData("th", "ไฟล์ทั้งหมดต้องมีขนาดรวมไม่เกิน 100 MB")]
    public async Task AuthenticatedMemberForm_RejectsOver100MibMultipartBeforeSubmission(
        string culture, string rejectionText)
    {
        await using var host = new MemberOrderBrowserHost();
        await host.StartAsync();
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = BrowserAuthenticationHandler.SchemeName },
        });
        await using var page = await context.NewPageAsync();
        page.SetDefaultTimeout(120_000);
        var response = await page.GotoAsync($"{host.Origin}/member/orders/3d-scanning?culture={culture}");
        Assert.Equal(200, response?.Status);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = Path.Combine(Path.GetTempPath(), $"member-order-browser-{Guid.NewGuid():N}.step");
        try
        {
            await using (var file = File.Create(path)) file.SetLength(100L * 1024 * 1024 + 1);
            await page.Locator("#member-order-files").SetInputFilesAsync(path);
            await page.Locator("#member-order-name").FillAsync("Test scan");
            await page.Locator("#member-order-width").FillAsync("1");
            await page.Locator("#member-order-length").FillAsync("1");
            await page.Locator("#member-order-height").FillAsync("1");
            await page.Locator("[data-terms-checkbox]").CheckAsync();
            await page.Locator("[data-terms-submit]").ClickAsync();

            var body = await page.Locator("body").InnerTextAsync();
            Assert.True(body.Contains(rejectionText, StringComparison.Ordinal),
                $"Expected localized size rejection at {page.Url}; body: {body[..Math.Min(body.Length, 1500)]}");
            Assert.Contains("/member/orders/3d-scanning", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, host.Submission.CallCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class MemberOrderBrowserHost : IAsyncDisposable
    {
        private WebApplicationFactory<Program>? factory;
        private HttpClient? client;
        public string Origin { get; private set; } = string.Empty;
        public BrowserSubmissionStub Submission { get; } = new();

        public async Task StartAsync()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            Origin = $"http://127.0.0.1:{port}";
            factory = new MemberTestingFactory(BrowserHostIdentityVerifier.SourceProjectDirectory(), Submission);
            factory.UseKestrel(port);
            client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri(Origin),
            });
            using var ready = await client.GetAsync("/health");
        }

        public async ValueTask DisposeAsync()
        {
            client?.Dispose();
            if (factory is not null) await factory.DisposeAsync();
        }
    }

    private sealed class MemberTestingFactory(string contentRoot, BrowserSubmissionStub submission)
        : TestingWebApplicationFactory(contentRoot)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICustomerOrderCatalogClient>();
                    services.RemoveAll<ICustomerOrderSubmissionService>();
                    services.AddSingleton<ICustomerOrderCatalogClient, BrowserCatalogStub>();
                    services.AddSingleton<ICustomerOrderSubmissionService>(submission);
                    services.AddAuthentication(options => options.DefaultAuthenticateScheme = BrowserAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, BrowserAuthenticationHandler>(
                            BrowserAuthenticationHandler.SchemeName, static _ => { });
                });
        }
    }

    private sealed class BrowserCatalogStub : ICustomerOrderCatalogClient
    {
        public Task<CustomerOrderCatalogResult> GetAsync(CustomerOrderKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerOrderCatalogResult(new CustomerOrderCatalog(
                [new CustomerOrderCatalogProcess(11, 2, "Structured light")], [], [],
                [new CustomerOrderFileFormat(1, "STEP", ".step")]), true, true));

        public Task<CustomerOrderMaterialOptionsResult> GetMaterialOptionsAsync(int materialId, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerOrderMaterialOptionsResult(null, true, false));
    }

    private sealed class BrowserSubmissionStub : ICustomerOrderSubmissionService
    {
        public int CallCount { get; private set; }

        public Task<CustomerOrderSubmissionResult> SubmitAsync(int trustedCustomerId, string trustedCustomerEmail,
            CustomerOrderDraft draft, Guid operationId, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Oversized browser upload must be rejected before submission.");
        }
    }

    private sealed class BrowserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "MemberOrderBrowserTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization, SchemeName, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "member-42"), new Claim(ClaimTypes.Email, "member@example.test")],
                SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
