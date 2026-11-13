using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MemberDashboard = Legacy.Maliev.Web.Areas.Member.Pages.Index;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks member overview composition and source notification semantics without downstream provider calls.</summary>
/// <param name="source">The borrowed normal Testing host factory.</param>
public sealed class MemberDashboardPageTests(TestingWebApplicationFactory source) : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task Get_ComposesOwnedProfileOrdersAndQuotationsFromSessionCustomer()
    {
        var account = new StubAccountClient(Profile());
        var orders = new StubOrderClient(new CustomerOrderPage([Order()], 1, 1, 1));
        var quotations = new StubQuotationClient(new CustomerQuotationPage([Quotation()], 1, 1, 1));
        var page = CreatePage(new StubSessionManager(42), account, orders, quotations);

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(42, account.CustomerId);
        Assert.Equal(42, orders.CustomerId);
        Assert.Equal(42, quotations.CustomerId);
        Assert.Equal(7, Assert.Single(page.RecentOrders).Id);
        Assert.Equal(9, Assert.Single(page.RecentQuotations).Id);
        Assert.Contains(page.Notices, value => value.Contains("billing address", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(page.Notices, value => value.Contains("open quotation", StringComparison.OrdinalIgnoreCase));
        Assert.Null(page.DisplayModel.BillingAddress);
        Assert.Null(page.DisplayModel.ShippingAddress);
        Assert.Equal(107, Assert.Single(page.DisplayModel.RecentQuotations).Total);
        Assert.EndsWith("T00:00:00.0000000", Assert.Single(page.DisplayModel.RecentOrders).CreatedDate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_MissingEncryptedSessionChallengesBeforeCallingServices()
    {
        var account = new StubAccountClient(Profile());
        var orders = new StubOrderClient(new CustomerOrderPage([], 1, 0, 0));
        var quotations = new StubQuotationClient(new CustomerQuotationPage([], 1, 0, 0));
        var page = CreatePage(new StubSessionManager(null), account, orders, quotations);

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.Null(account.CustomerId);
        Assert.Null(orders.CustomerId);
        Assert.Null(quotations.CustomerId);
    }

    /// <summary>Preserves source-assigned billing errors and shipping or quotation warnings through the loader.</summary>
    /// <param name="billing">Whether the stored profile has a billing address.</param>
    /// <param name="shipping">Whether the stored profile has a shipping address.</param>
    /// <param name="pending">Whether a stored quotation remains undecided.</param>
    /// <param name="warnings">The expected warning count.</param>
    /// <param name="errors">The expected error count.</param>
    /// <returns>The asynchronous loader regression.</returns>
    [Theory]
    [InlineData(false, false, true, 2, 1)]
    [InlineData(true, false, true, 2, 0)]
    [InlineData(true, true, true, 1, 0)]
    [InlineData(true, true, false, 0, 0)]
    public async Task Get_PropagatesSourceSeverityWithoutChangingNoticeText(
        bool billing, bool shipping, bool pending, int warnings, int errors)
    {
        var page = CreatePage(new StubSessionManager(42), new StubAccountClient(Profile(billing, shipping)),
            new StubOrderClient(new CustomerOrderPage([], 1, 0, 0)),
            new StubQuotationClient(new CustomerQuotationPage([Quotation() with { Accepted = pending ? null : true }], 1, 1, 1)));

        Assert.IsType<PageResult>(await page.OnGetAsync(CancellationToken.None));
        Assert.Equal(warnings, page.DisplayModel.NoticeDetails.Count(value => value.Severity == MemberOverviewNoticeSeverity.Warning));
        Assert.Equal(errors, page.DisplayModel.NoticeDetails.Count(value => value.Severity == MemberOverviewNoticeSeverity.Error));
        Assert.Equal(page.Notices, page.DisplayModel.NoticeDetails.Select(value => value.Content));
    }

    /// <summary>Keeps newer unavailable-service notices informational instead of guessing severity from their wording.</summary>
    /// <returns>The asynchronous loader regression.</returns>
    [Fact]
    public async Task Get_UnmappedAvailabilityNoticeRemainsInformational()
    {
        var page = CreatePage(new StubSessionManager(42), new StubAccountClient(null, false),
            new StubOrderClient(new CustomerOrderPage([], 1, 0, 0)),
            new StubQuotationClient(new CustomerQuotationPage([], 1, 0, 0)));

        Assert.IsType<PageResult>(await page.OnGetAsync(CancellationToken.None));
        var notice = Assert.Single(page.DisplayModel.NoticeDetails);
        Assert.Equal("Customer service is temporarily unavailable.", notice.Content);
        Assert.Equal(MemberOverviewNoticeSeverity.Information, notice.Severity);
        Assert.Equal(page.Notices, page.DisplayModel.NoticeDetails.Select(value => value.Content));
    }

    /// <summary>Renders the same severity-bearing loader output through active SSR and retained Razor routes in both languages.</summary>
    /// <param name="culture">The request language.</param>
    /// <param name="active">Whether the active SSR route owns the request.</param>
    /// <param name="billing">Whether the stored profile has a billing address.</param>
    /// <param name="panelSeverity">The expected highest source severity.</param>
    /// <param name="errorCount">The expected individual billing-error count.</param>
    /// <returns>The asynchronous actual-HTTP regression.</returns>
    [Theory]
    [InlineData("en", true, false, "error", 1)]
    [InlineData("th", true, false, "error", 1)]
    [InlineData("en", false, false, "error", 1)]
    [InlineData("th", false, false, "error", 1)]
    [InlineData("en", true, true, "warning", 0)]
    [InlineData("th", true, true, "warning", 0)]
    [InlineData("en", false, true, "warning", 0)]
    [InlineData("th", false, true, "warning", 0)]
    public async Task MemberOverview_ActualActiveAndFallbackHttpPreserveSeverity(
        string culture, bool active, bool billing, string panelSeverity, int errorCount)
    {
        await using var factory = source.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:MemberOverview", active.ToString());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccountSessionManager>();
                services.AddSingleton<IAccountSessionManager>(new StubSessionManager(42));
                services.RemoveAll<ICustomerAccountClient>();
                services.AddSingleton<ICustomerAccountClient>(new StubAccountClient(Profile(billing, false)));
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient>(new StubCountryClient());
                services.RemoveAll<ICustomerOrderClient>();
                services.AddSingleton<ICustomerOrderClient>(new StubOrderClient(new CustomerOrderPage([], 1, 0, 0)));
                services.RemoveAll<ICustomerQuotationClient>();
                services.AddSingleton<ICustomerQuotationClient>(new StubQuotationClient(new CustomerQuotationPage([Quotation()], 1, 1, 1)));
                services.AddAuthentication(options => options.DefaultAuthenticateScheme = NoticeAuthentication.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, NoticeAuthentication>(NoticeAuthentication.SchemeName, _ => { });
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(NoticeAuthentication.SchemeName);
        using var response = await client.GetAsync($"/member?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Equal(active, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        var panel = Regex.Match(html, "<section[^>]*data-member-notice-status=\"" + panelSeverity + "\"[^>]*>.*?</section>", RegexOptions.Singleline);
        Assert.True(panel.Success);
        Assert.Contains("maliev-alert--" + (panelSeverity == "error" ? "danger" : "warning"), panel.Value, StringComparison.Ordinal);
        Assert.Equal(errorCount, Regex.Matches(panel.Value, "data-member-notice-severity=\"error\"").Count);
        Assert.Equal(2, Regex.Matches(panel.Value, "data-member-notice-severity=\"warning\"").Count);
        Assert.Contains(culture == "th" ? "การแจ้งเตือนบัญชี" : "Account notices", panel.Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "เพิ่มที่อยู่จัดส่งก่อนส่งคำสั่งซื้อ" : "Add a shipping address before an order is dispatched.", panel.Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "คุณมีใบเสนอราคาที่รอตรวจสอบ" : "You have an open quotation to review.", panel.Value, StringComparison.Ordinal);
        if (!billing)
        {
            Assert.Contains(culture == "th" ? "เพิ่มที่อยู่วางบิลเพื่อให้ข้อมูลบัญชีครบถ้วน" : "Add a billing address to complete your account.", panel.Value, StringComparison.Ordinal);
            Assert.Contains("fa-exclamation-circle", panel.Value, StringComparison.Ordinal);
        }
        Assert.Contains("fa-exclamation-triangle", panel.Value, StringComparison.Ordinal);
        Assert.Contains("data-account-setup-notice", html, StringComparison.Ordinal);
    }

    /// <summary>Retains the original unknown-severity icon fallback and the existing string-only caller projection with encoded content.</summary>
    /// <param name="culture">The render language.</param>
    /// <param name="typed">Whether the caller supplies an unknown typed severity.</param>
    /// <returns>The asynchronous real-component render regression.</returns>
    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task NoticeRenderer_UnknownSeverityAndStringProjectionRemainSafe(string culture, bool typed)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var model = MemberOverviewDisplayModel.Empty with
            {
                Notices = ["<script>notice</script>"],
                NoticeDetails = typed ? [new("<script>notice</script>", (MemberOverviewNoticeSeverity)99)] : [],
            };
            using var services = new ServiceCollection().AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources").BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(MemberOverviewContent.Model)] = model,
                });
                return (await renderer.RenderComponentAsync<MemberOverviewContent>(parameters)).ToHtmlString();
            });

            Assert.Contains("data-member-notice-severity=\"" + (typed ? "unknown" : "information") + "\"", html, StringComparison.Ordinal);
            Assert.Contains(typed ? "fa-question-circle" : "fa-info-circle", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;notice&lt;/script&gt;", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static CustomerAccountDetails Profile(bool billing, bool shipping) => Profile() with
    {
        BillingAddress = billing ? Address() : null,
        ShippingAddress = shipping ? Address() : null,
    };

    private static CustomerAddress Address() => new(1, null, "Fixture address", null, "Bangkok", null, "10110", 1, null, null);

    private sealed class NoticeAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "MemberNoticeFixture";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != SchemeName)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "member-notice-fixture")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private static MemberDashboard CreatePage(
        IAccountSessionManager session,
        ICustomerAccountClient account,
        ICustomerOrderClient orders,
        ICustomerQuotationClient quotations) => new(session, account, new StubCountryClient(), orders, quotations)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

    private static CustomerAccountDetails Profile() => new(
        42,
        "Local",
        "Customer",
        "Local Customer",
        null,
        null,
        null,
        "local@example.test",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    private static CustomerOrder Order() => new(
        7,
        42,
        "CNC part",
        null,
        3,
        2,
        0,
        2,
        50,
        0,
        100,
        5,
        null,
        null,
        null,
        true,
        false,
        null,
        new DateTime(2026, 7, 15),
        null);

    private static CustomerQuotation Quotation() => new(
        9,
        42,
        null,
        30,
        new DateTime(2026, 12, 31),
        100,
        7,
        107,
        3,
        104,
        764,
        null,
        null,
        null,
        null,
        null,
        new DateTime(2026, 7, 15),
        null);

    private sealed class StubSessionManager(int? customerId) : IAccountSessionManager
    {
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult(customerId);

        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubAccountClient(CustomerAccountDetails? profile, bool serviceAvailable = true) : ICustomerAccountClient
    {
        public int? CustomerId { get; private set; }

        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            return Task.FromResult(new CustomerAccountProfileResult(profile, serviceAvailable, true));
        }

        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubOrderClient(CustomerOrderPage page) : ICustomerOrderClient
    {
        public int? CustomerId { get; private set; }

        public Task<CustomerOrderListResult> ListAsync(int customerId, string? sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            return Task.FromResult(new CustomerOrderListResult(page, true, true));
        }

        public Task<CustomerOrderDetailsResult> GetAsync(int customerId, int orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerOrderOperationResult> CancelAsync(int customerId, int orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([], true));
    }

    private sealed class StubQuotationClient(CustomerQuotationPage page) : ICustomerQuotationClient
    {
        public int? CustomerId { get; private set; }

        public Task<CustomerQuotationListResult> ListAsync(int customerId, string? sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            return Task.FromResult(new CustomerQuotationListResult(page, true, true));
        }

        public Task<CustomerQuotationDetailsResult> GetAsync(int customerId, int quotationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerQuotationDecisionResult> DecideAsync(int customerId, int quotationId, bool accepted, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
