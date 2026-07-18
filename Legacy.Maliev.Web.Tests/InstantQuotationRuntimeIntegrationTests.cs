using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationRuntimeIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly IDataProtectionProvider DataProtectionProvider = new EphemeralDataProtectionProvider();
    private readonly WebApplicationFactory<Program> factory;

    public InstantQuotationRuntimeIntegrationTests(WebApplicationFactory<Program> factory) =>
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));

    [Fact]
    public void Runtime_RegistersAuthoritativePricingAndServerOnlySessionIdentity()
    {
        using var scope = factory.Services.CreateScope();

        Assert.IsType<InstantQuotationPricingService>(
            scope.ServiceProvider.GetRequiredService<IInstantQuotationPricingService>());
        Assert.IsType<ProtectedCookieInstantQuotationWorkflowSessionIdentityAccessor>(
            scope.ServiceProvider.GetRequiredService<IInstantQuotationWorkflowSessionIdentityAccessor>());
    }

    [Fact]
    public async Task SessionIdentity_RoundTripsOnlyThroughProtectedHostCookie()
    {
        var firstContext = new DefaultHttpContext();
        var firstAccessor = CreateAccessor(firstContext);
        var identity = new string('A', 64);

        await firstAccessor.SetProtectedSessionIdentityAsync(identity, default);

        var setCookie = Assert.Single(firstContext.Response.Headers.SetCookie);
        Assert.NotNull(setCookie);
        Assert.Contains("__Host-Maliev.InstantQuotation=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(identity, setCookie, StringComparison.Ordinal);

        var secondContext = new DefaultHttpContext();
        secondContext.Request.Headers.Cookie = setCookie!.Split(';', 2)[0];
        var restored = await CreateAccessor(secondContext).GetProtectedSessionIdentityAsync(default);

        Assert.Equal(identity, restored);
    }

    [Theory]
    [InlineData("not-protected")]
    [InlineData("AQAA-tampered")]
    public async Task SessionIdentity_TamperingFailsClosed(string cookieValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"__Host-Maliev.InstantQuotation={cookieValue}";

        var restored = await CreateAccessor(context).GetProtectedSessionIdentityAsync(default);

        Assert.Null(restored);
    }

    private static ProtectedCookieInstantQuotationWorkflowSessionIdentityAccessor CreateAccessor(
        HttpContext context)
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = context };
        return new ProtectedCookieInstantQuotationWorkflowSessionIdentityAccessor(
            httpContextAccessor,
            DataProtectionProvider,
            NullLogger<ProtectedCookieInstantQuotationWorkflowSessionIdentityAccessor>.Instance);
    }
}
