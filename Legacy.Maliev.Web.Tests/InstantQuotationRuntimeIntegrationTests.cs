using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationRuntimeIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly IDataProtectionProvider DataProtectionProvider = new EphemeralDataProtectionProvider();
    private readonly WebApplicationFactory<Program> factory;

    public InstantQuotationRuntimeIntegrationTests(WebApplicationFactory<Program> factory) =>
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });

    [Fact]
    public void Runtime_RegistersAuthoritativePricingAndServerOnlySessionIdentity()
    {
        using var scope = factory.Services.CreateScope();

        Assert.IsType<InstantQuotationPricingService>(
            scope.ServiceProvider.GetRequiredService<IInstantQuotationPricingService>());
        Assert.IsType<AuthenticationStateInstantQuotationWorkflowSessionIdentityAccessor>(
            scope.ServiceProvider.GetRequiredService<IInstantQuotationWorkflowSessionIdentityAccessor>());
    }

    [Fact]
    public async Task Route_FirstRequestEstablishesProtectedCookie_AndReloadDoesNotExtendIt()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

        using var first = await client.GetAsync("/InstantQuotation/3D-Printing?culture=en");
        var setCookie = Assert.Single(
            first.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Maliev.InstantQuotation=", StringComparison.Ordinal));

        Assert.Contains("__Host-Maliev.InstantQuotation=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        var maxAge = Regex.Match(setCookie, "max-age=(?<seconds>[0-9]+)", RegexOptions.IgnoreCase);
        Assert.True(maxAge.Success);
        Assert.InRange(int.Parse(maxAge.Groups["seconds"].Value), 86_390, 86_400);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);

        using var second = await client.GetAsync("/InstantQuotation/3D-Printing?culture=en");
        var secondCookies = second.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

        Assert.DoesNotContain(
            secondCookies,
            value => value.StartsWith("__Host-Maliev.InstantQuotation=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-protected")]
    [InlineData("%%%malformed%%%")]
    public async Task Route_MalformedCookieIsReplacedWithoutRenderingIdentity(string cookieValue)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/InstantQuotation/3D-Printing?culture=en");
        request.Headers.Add("Cookie", $"__Host-Maliev.InstantQuotation={cookieValue}");

        using var response = await client.SendAsync(request);
        var source = await response.Content.ReadAsStringAsync();
        var replacement = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Maliev.InstantQuotation=", StringComparison.Ordinal));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(cookieValue, replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("instant-quotation-session", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cookieValue, source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Route_ThrowingCountryDependencyFallsBackWithoutBlockingCookieOrAntiforgery()
    {
        using var resilientFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICountryClient>();
            services.AddSingleton<ICountryClient, ThrowingCountryClient>();
        }));
        using var client = resilientFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/InstantQuotation/3D-Printing?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", source, StringComparison.Ordinal);
        Assert.Contains("data-workflow-upload", source, StringComparison.Ordinal);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Maliev.InstantQuotation=", StringComparison.Ordinal));
        Assert.DoesNotContain("country dependency failed", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CircuitAccessor_ReadsIdentityFromAuthenticationState_AndCannotReplaceIt()
    {
        var identity = new string('A', 64);
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(InstantQuotationSessionIdentityClaim.Type, identity) }));
        var accessor = new AuthenticationStateInstantQuotationWorkflowSessionIdentityAccessor(
            new StaticAuthenticationStateProvider(principal));

        Assert.Equal(identity, await accessor.GetProtectedSessionIdentityAsync(default));
        await accessor.SetProtectedSessionIdentityAsync(identity, default);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await accessor.SetProtectedSessionIdentityAsync(new string('B', 64), default));
    }

    [Fact]
    public void Cookie_ValidProtectedPayloadWithInvalidIdentityFailsClosed()
    {
        var context = new DefaultHttpContext();
        var protectedValue = DataProtectionProvider
            .CreateProtector(InstantQuotationSessionIdentityCookie.ProtectorPurpose)
            .Protect("validly-protected-but-not-an-identity");
        context.Request.Headers.Cookie = $"{InstantQuotationSessionIdentityCookie.CookieName}={protectedValue}";
        var cookie = new InstantQuotationSessionIdentityCookie(
            DataProtectionProvider,
            TimeProvider.System,
            NullLogger<InstantQuotationSessionIdentityCookie>.Instance);

        Assert.Null(cookie.TryRead(context));
    }

    private sealed class StaticAuthenticationStateProvider(System.Security.Claims.ClaimsPrincipal principal)
        : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }

    private sealed class ThrowingCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            throw new HttpRequestException("country dependency failed");
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "TH", "TH", "THA", null, null)],
                true));
    }
}
