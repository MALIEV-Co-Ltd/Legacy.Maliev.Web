using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises member challenges with the application's cookie handler and synthetic session data.</summary>
public sealed class MemberCookieChallengeTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData(true, "/member/account/manage/profile", "UpdateProfile")]
    [InlineData(false, "/member/account/manage/profile", "UpdateProfile")]
    [InlineData(true, "/member/account/manage/address", "UpdateAddress")]
    [InlineData(false, "/member/account/manage/address", "UpdateAddress")]
    [InlineData(true, "/member/account/manage/changeemail", "ChangeEmail")]
    [InlineData(false, "/member/account/manage/changeemail", "ChangeEmail")]
    [InlineData(true, "/member/account/manage/changepassword", "ChangePassword")]
    [InlineData(false, "/member/account/manage/changepassword", "ChangePassword")]
    [InlineData(true, "/member/account/manage/createpassword", "CreatePassword")]
    [InlineData(false, "/member/account/manage/createpassword", "CreatePassword")]
    public async Task AuthenticatedCookieWithMissingOwnedSessionRedirectsBeforeForm(bool active, string path, string handler)
    {
        await using var host = CreateHost(active, sessionExists: true);
        using var client = CreateClientWithTicket(host, expired: false);
        using var response = await client.GetAsync(path + "?culture=en");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.DoesNotContain("handler=" + handler, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.True(host.Services.GetRequiredService<StubSessionManager>().Reads > 0);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public async Task MissingStoreSessionOrExpiredCookieRedirects(bool active, bool sessionExists, bool expired)
    {
        await using var host = CreateHost(active, sessionExists);
        using var client = CreateClientWithTicket(host, expired);
        using var response = await client.GetAsync("/member/account/manage/changeemail?culture=en");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CreatePasswordPost_PreservesAntiforgeryAndRazorHandler(bool active, bool withAntiforgery)
    {
        const string path = "/member/account/manage/createpassword?culture=en";
        const string password = "SyntheticTestPassword123!";
        var authentication = DispatchProxy.Create<ICustomerAuthenticationClient, CreatePasswordProbe>();
        var probe = (CreatePasswordProbe)authentication;
        await using var host = CreateHost(active, sessionExists: true, authentication);
        using var client = CreateClientWithTicket(host, expired: false);
        using var get = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var html = await get.Content.ReadAsStringAsync();
        Assert.Equal(active, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        var verification = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(verification.Success);
        var fields = new Dictionary<string, string>
        {
            ["Password"] = password,
            ["ConfirmPassword"] = password,
        };
        if (withAntiforgery)
        {
            fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(verification.Groups[1].Value);
        }

        using var post = await client.PostAsync(path + "&handler=CreatePassword", new FormUrlEncodedContent(fields));
        Assert.Equal(withAntiforgery ? HttpStatusCode.OK : HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Equal(withAntiforgery ? 1 : 0, probe.Calls);
        if (withAntiforgery)
        {
            var result = WebUtility.HtmlDecode(await post.Content.ReadAsStringAsync());
            Assert.Contains("This account already has a password.", result, StringComparison.Ordinal);
            Assert.DoesNotContain(password, result, StringComparison.Ordinal);
        }
    }

    public class CreatePasswordProbe : DispatchProxy
    {
        public int Calls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ICustomerAuthenticationClient.CreatePasswordAsync))
                throw new InvalidOperationException("Unexpected authentication operation.");
            Calls++;
            return Task.FromResult(new CustomerPasswordCreationResult(false, true, true, true));
        }
    }

    private WebApplicationFactory<Program> CreateHost(
        bool active, bool sessionExists, ICustomerAuthenticationClient? authentication = null) => factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("BlazorRouting:Home", active.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccountSessionStore>();
            services.RemoveAll<IAccountSessionManager>();
            services.RemoveAll<ICustomerAccountClient>();
            services.RemoveAll<ICountryClient>();
            services.RemoveAll<ICustomerAuthenticationClient>();
            services.AddSingleton<IAccountSessionStore>(new SyntheticStore(sessionExists));
            services.AddSingleton(new StubSessionManager(authentication is null ? null : "synthetic-access"));
            services.AddSingleton<IAccountSessionManager>(provider => provider.GetRequiredService<StubSessionManager>());
            services.AddSingleton(DispatchProxy.Create<ICustomerAccountClient, RefuseCalls>());
            services.AddSingleton(DispatchProxy.Create<ICountryClient, RefuseCalls>());
            services.AddSingleton(authentication ?? DispatchProxy.Create<ICustomerAuthenticationClient, RefuseCalls>());
        });
    });

    private static HttpClient CreateClientWithTicket(WebApplicationFactory<Program> host, bool expired)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var options = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "customer:42"),
            new Claim(AccountSessionManager.SessionIdClaim, "synthetic-session"),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1),
        };
        var ticket = new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }

    private sealed class SyntheticStore(bool exists) : IAccountSessionStore
    {
        public Task<AccountSession?> GetAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<AccountSession?>(exists && sessionId == "synthetic-session"
                ? new AccountSession("synthetic@example.invalid", 42, "synthetic-token", "synthetic-refresh", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddDays(1))
                : null);
        public Task SetAsync(string sessionId, AccountSession session, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IAsyncDisposable?> AcquireRefreshLockAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubSessionManager(string? accessToken) : IAccountSessionManager
    {
        public int Reads { get; private set; }
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken token) { Reads++; return Task.FromResult<int?>(null); }
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken token) { Reads++; return Task.FromResult(accessToken); }
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken token) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken token) => throw new NotSupportedException();
    }

    public class RefuseCalls : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Downstream calls are forbidden in missing-session tests.");
    }
}
