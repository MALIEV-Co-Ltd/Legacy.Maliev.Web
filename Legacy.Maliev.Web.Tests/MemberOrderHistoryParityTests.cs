using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberOrderHistoryParityTests : IClassFixture<TestingWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    private readonly Orders orders = new();

    public MemberOrderHistoryParityTests(TestingWebApplicationFactory source)
    {
        factory = source.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICustomerOrderClient>();
            services.AddSingleton<ICustomerOrderClient>(orders);
            services.RemoveAll<IAccountSessionManager>();
            services.AddSingleton<IAccountSessionManager, Sessions>();
            services.AddAuthentication(options => options.DefaultAuthenticateScheme = HistoryAuthentication.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, HistoryAuthentication>(HistoryAuthentication.SchemeName, _ => { });
        }));
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Theory]
    [InlineData("en", "OrderId_Ascending", "Order ID ascending", "Order ID descending")]
    [InlineData("en", "OrderId_Descending", "Order ID ascending", "Order ID descending")]
    [InlineData("th", "OrderId_Ascending", "ID น้อยไปมาก", "ID มากไปน้อย")]
    [InlineData("th", "OrderId_Descending", "ID น้อยไปมาก", "ID มากไปน้อย")]
    public async Task IdSort_IsSelectableLocalizedAndRetainedAcrossNavigation(
        string culture, string sort, string ascendingLabel, string descendingLabel)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(HistoryAuthentication.SchemeName);
        const string search = "CNC & fixture";
        var route = QueryHelpers.AddQueryString("/member/orders/history", new Dictionary<string, string?>
        {
            ["culture"] = culture,
            ["sort"] = sort,
            ["search"] = search,
            ["size"] = "50",
            ["index"] = "2"
        });

        using var response = await client.GetAsync(route);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var selector = Regex.Match(html, "<select id=\"member-order-sort\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
        Assert.True(selector.Success);
        Assert.Contains($">{ascendingLabel}</option>", selector.Value, StringComparison.Ordinal);
        Assert.Contains($">{descendingLabel}</option>", selector.Value, StringComparison.Ordinal);
        Assert.Contains($"value=\"{sort}\" selected", selector.Value, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Matches(selector.Value, @"\sselected(?:\s|=|>)").Count);
        foreach (var retained in new[] { "OrderCreatedDate_Descending", "OrderCreatedDate_Ascending", "OrderName_Ascending", "OrderName_Descending" })
            Assert.Contains($"value=\"{retained}\"", selector.Value, StringComparison.Ordinal);
        Assert.Contains("noindex,follow", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/member/orders/view?itemID=7\"", html, StringComparison.Ordinal);
        Assert.Equal(new Invocation(42, sort, search, 2, 50), orders.LastInvocation);

        var links = Regex.Matches(html, @"href=""(/member/orders/history\?[^""]+)""")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            var query = QueryHelpers.ParseQuery(new Uri(client.BaseAddress!, link).Query);
            Assert.Equal(sort, query["sort"].ToString());
            Assert.Equal(search, query["search"].ToString());
            Assert.Equal("50", query["size"].ToString());
        }
        var next = links.First(link => QueryHelpers.ParseQuery(new Uri(client.BaseAddress!, link).Query)["index"] == "3");
        using var nextResponse = await client.GetAsync(next);
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        Assert.Equal(new Invocation(42, sort, search, 3, 50), orders.LastInvocation);
    }

    [Theory]
    [InlineData("en", "OrderId_Ascending")]
    [InlineData("en", "OrderId_Descending")]
    [InlineData("th", "OrderId_Ascending")]
    [InlineData("th", "OrderId_Descending")]
    public async Task IdSort_AnonymousRequestRedirectsWithoutOrderRead(string culture, string sort)
    {
        using var response = await client.GetAsync($"/member/orders/history?culture={culture}&sort={sort}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/account/login", response.Headers.Location.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Null(orders.LastInvocation);
    }

    [Theory]
    [InlineData("OrderId_Ascending", "invalid")]
    [InlineData("OrderId_Descending", "invalid")]
    [InlineData("OrderId_Ascending", "0")]
    [InlineData("OrderId_Descending", "0")]
    [InlineData("OrderId_Ascending", "-1")]
    [InlineData("OrderId_Descending", "-1")]
    [InlineData("OrderId_Ascending", "101")]
    [InlineData("OrderId_Descending", "101")]
    [InlineData("OrderId_Ascending", "2147483648")]
    [InlineData("OrderId_Descending", "2147483648")]
    public async Task IdSort_InvalidSizeDoesNotReadOrders(string sort, string size)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(HistoryAuthentication.SchemeName);
        using var response = await client.GetAsync($"/member/orders/history?sort={sort}&size={size}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(orders.LastInvocation);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private sealed record Invocation(int CustomerId, string? Sort, string? Search, int Index, int Size);

    private sealed class Orders : ICustomerOrderClient
    {
        public Invocation? LastInvocation { get; private set; }

        public Task<CustomerOrderListResult> ListAsync(int customerId, string? sort, string? search,
            int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            LastInvocation = new(customerId, sort, search, pageIndex, pageSize);
            var order = new CustomerOrder(7, customerId, "Fixture", null, 3, 2, 0, 2, 100, 0, 200,
                null, null, null, null, false, false, null, new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), null);
            return Task.FromResult(new CustomerOrderListResult(new CustomerOrderPage([order], pageIndex, 3, 1), true, true));
        }

        public Task<CustomerOrderDetailsResult> GetAsync(int customerId, int orderId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CustomerOrderOperationResult> CancelAsync(int customerId, int orderId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Sessions : IAccountSessionManager
    {
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<int?>(context.User.Identity?.IsAuthenticated == true ? 42 : null);

        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password,
            bool rememberMe, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class HistoryAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "MemberHistoryTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization, SchemeName, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "customer:42"),
                new Claim("identity_kind", "customer"),
                new Claim("legacy_database_id", "42")
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
