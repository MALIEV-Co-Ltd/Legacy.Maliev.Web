using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InitialPasswordBlazorRouteTests(TestingWebApplicationFactory factory) : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetEndpoint_RetainsAccountRateLimit(bool active)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:InitialPassword", active.ToString()));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var routes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var endpoint = Assert.Single(routes, route =>
        {
            var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            return string.Equals(route.RoutePattern.RawText?.TrimStart('/'), "Account/SetInitialPassword", StringComparison.OrdinalIgnoreCase) &&
                (methods is null || methods.Count == 0 || methods.Contains("GET", StringComparer.OrdinalIgnoreCase));
        });
        Assert.Equal("account", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task Get_ValidChallenge_PreservesProtectedForm(bool active, string culture)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:InitialPassword", active.ToString()));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync($"/Account/SetInitialPassword?culture={culture}&email=customer%40example.com&token=synthetic-token&returnUrl=%2FMember&rememberMe=true");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"ReturnUrl\" value=\"/Member\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"RememberMe\" value=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"robots\" content=\"noindex, nofollow\"", html, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(active, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_MissingChallenge_RedirectsToLogin(bool active)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:InitialPassword", active.ToString()));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/Account/SetInitialPassword?email=customer%40example.com");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath, ignoreCase: true);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Theory]
    [InlineData(true, "//outside.example")]
    [InlineData(true, "/\\outside.example")]
    [InlineData(true, "~/\\outside.example")]
    [InlineData(true, "https://outside.example")]
    [InlineData(true, "/local\nheader")]
    [InlineData(false, "//outside.example")]
    [InlineData(false, "/\\outside.example")]
    [InlineData(false, "~/\\outside.example")]
    [InlineData(false, "https://outside.example")]
    [InlineData(false, "/local\nheader")]
    public async Task Get_UnsafeReturnUrl_IsNotPropagated(bool active, string returnUrl)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:InitialPassword", active.ToString()));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync($"/Account/SetInitialPassword?email=customer%40example.com&token=synthetic-token&returnUrl={Uri.EscapeDataString(returnUrl)}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(returnUrl, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_WithoutAntiforgery_IsRejected(bool active)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:InitialPassword", active.ToString()));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsync("/Account/SetInitialPassword?handler=Complete", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_WithAntiforgery_ReachesRetainedHandlerOnceAndRerendersFailure(bool active)
    {
        var fake = DispatchProxy.Create<ICustomerAuthenticationClient, RejectingAuthenticationProxy>();
        var proxy = (RejectingAuthenticationProxy)fake;
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:InitialPassword", active.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICustomerAuthenticationClient>();
                services.AddSingleton(fake);
            });
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var challenge = new string('t', 43);
        using var get = await client.GetAsync($"/Account/SetInitialPassword?culture=en&email=customer%40example.com&token={challenge}");
        var html = await get.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);

        using var response = await client.PostAsync("/Account/SetInitialPassword?handler=Complete&culture=en", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["Email"] = "customer@example.com",
            ["Token"] = challenge,
            ["Password"] = "SyntheticPassword123!",
            ["ConfirmPassword"] = "SyntheticPassword123!",
        }));
        var result = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, proxy.CompletionCalls);
        Assert.Contains("This password setup link is invalid or has expired.", result, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    public class RejectingAuthenticationProxy : DispatchProxy
    {
        public int CompletionCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ICustomerAuthenticationClient.CompleteInitialPasswordAsync))
                throw new InvalidOperationException("Unexpected authentication operation.");
            CompletionCalls++;
            return Task.FromResult(false);
        }
    }
}
