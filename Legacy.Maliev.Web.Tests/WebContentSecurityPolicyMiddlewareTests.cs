using System.Net;
using Legacy.Maliev.Web.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Guards response-starting policy writes when the same middleware runs twice for one HTTP request.</summary>
public sealed class WebContentSecurityPolicyMiddlewareTests
{
    /// <summary>Requires both middleware passes to continue while the real response emits one complete document policy.</summary>
    /// <returns>A task that completes after the actual TestServer response and header assertions.</returns>
    [Fact]
    public async Task SameRequest_TwoMiddlewarePassesEmitExactlyOneDocumentPolicy()
    {
        var nextInvocations = 0;
        using var server = new TestServer(new WebHostBuilder().Configure(app =>
        {
            app.Run(async context =>
            {
                var middleware = new WebContentSecurityPolicyMiddleware(_ =>
                {
                    nextInvocations++;
                    return Task.CompletedTask;
                });

                await middleware.InvokeAsync(context);
                await middleware.InvokeAsync(context);
                await context.Response.WriteAsync("double-pass");
            });
        }));
        using var client = server.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("double-pass", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, nextInvocations);
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Equal(WebContentSecurityPolicy.DocumentPolicy, policy);
        Assert.Contains("'wasm-unsafe-eval'", policy, StringComparison.Ordinal);
    }
}
