using System.Net;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises the Web forwarded-header trust decision through actual options and middleware.</summary>
public sealed class ForwardedHeaderTrustTests
{
    /// <summary>Checks empty-list rejection and retained explicit, mapped, loopback and network trust.</summary>
    /// <param name="environment">The host environment used by the existing shared registration.</param>
    /// <param name="trust">The explicit trust-list control, if any.</param>
    /// <param name="peer">The actual non-header connection address, or null for the compatibility control.</param>
    /// <param name="expectedPeer">The connection address after the real middleware executes.</param>
    /// <param name="expectedScheme">The request scheme after the real middleware executes.</param>
    /// <returns>The asynchronous middleware assertion.</returns>
    [Theory]
    [InlineData("Production", "none", "198.51.100.9", "198.51.100.9", "http")]
    [InlineData("Staging", "none", "198.51.100.9", "198.51.100.9", "http")]
    [InlineData("Production", "proxy", "203.0.113.7", "192.0.2.50", "https")]
    [InlineData("Production", "proxy", "198.51.100.9", "198.51.100.9", "http")]
    [InlineData("Production", "proxy", "::ffff:203.0.113.7", "192.0.2.50", "https")]
    [InlineData("Production", "network", "203.0.113.7", "192.0.2.50", "https")]
    [InlineData("Production", "network", "198.51.100.9", "198.51.100.9", "http")]
    [InlineData("Development", "none", "127.0.0.1", "192.0.2.50", "https")]
    [InlineData("Testing", "none", "127.0.0.1", "192.0.2.50", "https")]
    [InlineData("Testing", "none", "198.51.100.9", "198.51.100.9", "http")]
    [InlineData("Production", "none", null, null, "http")]
    [InlineData("Production", "proxy", null, "192.0.2.50", "https")]
    public async Task MiddlewarePreservesExplicitTrustAndRejectsEmptyLists(
        string environment, string trust, string? peer, string? expectedPeer, string expectedScheme)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.AddStandardMiddleware();
        if (trust == "proxy") builder.Configuration["ForwardedHeaders:KnownProxies:0"] = "203.0.113.7";
        if (trust == "network") builder.Services.Configure<ForwardedHeadersOptions>(options =>
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("203.0.113.0/24")));
        builder.Services.AddWebForwardedHeaderTrust();
        using var host = builder.Build();
        var provider = host.Services;
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "192.0.2.50";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        var application = new ApplicationBuilder(provider);
        application.UseForwardedHeaders();
        application.Run(_ => Task.CompletedTask);
        await application.Build()(context);
        Assert.Equal(expectedPeer is null ? null : IPAddress.Parse(expectedPeer), context.Connection.RemoteIpAddress);
        Assert.Equal(expectedScheme, context.Request.Scheme);
        var accepted = expectedScheme == "https";
        Assert.Equal(!accepted, context.Request.Headers.ContainsKey("X-Forwarded-For"));
        Assert.Equal(!accepted, context.Request.Headers.ContainsKey("X-Forwarded-Proto"));
    }

    /// <summary>Preserves the framework's right-to-left chain validation and configured forwarding limit.</summary>
    /// <param name="intermediateTrusted">Whether the intermediate hop is explicitly trusted.</param>
    /// <returns>The asynchronous chain assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForwardedChainStopsAtTheFirstUntrustedHop(bool intermediateTrusted)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Configuration["ForwardedHeaders:KnownProxies:0"] = "203.0.113.7";
        builder.AddStandardMiddleware();
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardLimit = 2;
            if (intermediateTrusted) options.KnownProxies.Add(IPAddress.Parse("203.0.113.8"));
        });
        builder.Services.AddWebForwardedHeaderTrust();
        using var host = builder.Build();
        var provider = host.Services;
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "192.0.2.50, 203.0.113.8";
        context.Request.Headers["X-Forwarded-Proto"] = "http, https";
        var application = new ApplicationBuilder(provider);
        application.UseForwardedHeaders();
        application.Run(_ => Task.CompletedTask);
        await application.Build()(context);
        Assert.Equal(IPAddress.Parse(intermediateTrusted ? "192.0.2.50" : "203.0.113.8"), context.Connection.RemoteIpAddress);
        Assert.Equal(intermediateTrusted ? "http" : "https", context.Request.Scheme);
        Assert.Equal(intermediateTrusted ? string.Empty : "192.0.2.50", context.Request.Headers["X-Forwarded-For"].ToString());
    }

    /// <summary>Checks late ordinary configuration is evaluated before the final trust decision.</summary>
    [Fact]
    public void LateConfigureCannotLeaveAnEmptyTrustListWithForwardingEnabled()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.AddStandardMiddleware();
        builder.Services.AddWebForwardedHeaderTrust();
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            options.ForwardedHeaders = ForwardedHeaders.All;
        });
        using var host = builder.Build();
        var provider = host.Services;
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
    }

    /// <summary>Checks the framework's configured automatic middleware uses the same final DI options.</summary>
    /// <param name="explicitProxy">Whether the actual connection peer is explicitly trusted.</param>
    /// <returns>The asynchronous real-host assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrameworkAutomaticForwardingUsesFinalOptions(bool explicitProxy)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            Args = ["--ForwardedHeaders_Enabled=true"],
        });
        builder.WebHost.UseTestServer();
        builder.AddStandardMiddleware();
        if (explicitProxy) builder.Configuration["ForwardedHeaders:KnownProxies:0"] = "203.0.113.7";
        builder.Services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new ConnectionPeerFilter()));
        builder.Services.AddWebForwardedHeaderTrust();
        await using var application = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        ObservedRequest? observation = null;
        application.Run(context =>
        {
            observation = new ObservedRequest(context.Connection.RemoteIpAddress?.ToString(), context.Request.Scheme,
                context.Request.Headers.ContainsKey("X-Forwarded-For"), context.Request.Headers.ContainsKey("X-Forwarded-Proto"));
            return Task.CompletedTask;
        });
        await application.StartAsync(timeout.Token);
        using var client = application.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "192.0.2.50");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(explicitProxy ? "192.0.2.50" : "203.0.113.7", observation?.Peer);
        Assert.Equal(explicitProxy ? "https" : "http", observation?.Scheme);
        Assert.Equal(!explicitProxy, observation?.RetainedForwardedFor);
        Assert.Equal(!explicitProxy, observation?.RetainedForwardedProto);
    }

    /// <summary>Exercises the real Program registration and request pipeline without adding a runtime endpoint.</summary>
    /// <param name="explicitProxy">Whether the actual connection address is configured as trusted.</param>
    /// <returns>The asynchronous existing-health-route assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualProgramUsesTheFinalTrustDecision(bool explicitProxy)
    {
        using var factory = new TrustFactory(explicitProxy);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/web/liveness");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "192.0.2.50");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(explicitProxy ? "192.0.2.50" : "203.0.113.7", factory.Observation?.Peer);
        Assert.Equal(explicitProxy ? "https" : "http", factory.Observation?.Scheme);
        Assert.Equal(!explicitProxy, factory.Observation?.RetainedForwardedFor);
        Assert.Equal(!explicitProxy, factory.Observation?.RetainedForwardedProto);
    }

    private sealed record ObservedRequest(string? Peer, string Scheme, bool RetainedForwardedFor, bool RetainedForwardedProto);

    private sealed class ConnectionPeerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
        {
            application.Use((context, following) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
                return following(context);
            });
            next(application);
        };
    }

    private sealed class TrustFactory(bool explicitProxy) : TestingWebApplicationFactory
    {
        internal ObservedRequest? Observation { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.KnownProxies.Clear();
                    options.KnownIPNetworks.Clear();
                    if (explicitProxy) options.KnownProxies.Add(IPAddress.Parse("203.0.113.7"));
                });
                services.AddSingleton<IStartupFilter>(new ObservationFilter(this));
            });
        }

        private sealed class ObservationFilter(TrustFactory owner) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
            {
                application.Use(async (context, following) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
                    await following(context);
                    owner.Observation = new ObservedRequest(context.Connection.RemoteIpAddress?.ToString(),
                        context.Request.Scheme, context.Request.Headers.ContainsKey("X-Forwarded-For"),
                        context.Request.Headers.ContainsKey("X-Forwarded-Proto"));
                });
                next(application);
            };
        }
    }
}
