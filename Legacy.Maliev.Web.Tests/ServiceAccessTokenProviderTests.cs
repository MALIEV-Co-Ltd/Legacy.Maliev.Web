using System.Net;
using System.Text;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceAccessTokenProviderTests
{
    [Fact]
    public async Task Token_IsAcquiredOnceAndCachedBeforeExpiry()
    {
        var handler = new CountingHandler(
            """
            {"accessToken":"jwt-value","tokenType":"Bearer","expiresIn":7200,"user":{"userId":"legacy-web","userType":"service"}}
            """);
        var provider = CreateProvider(handler);

        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("jwt-value", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("/auth/v1/service/login", handler.LastRequestUri?.AbsolutePath);
        Assert.Contains("\"clientId\":\"service-production-legacy-web\"", handler.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("jwt-value", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_ConfigurationMissingFailsClosedWithoutNetworkCall()
    {
        var handler = new CountingHandler("{}");
        var provider = CreateProvider(handler, clientSecret: string.Empty);

        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Null(token);
        Assert.Equal(0, handler.CallCount);
    }

    private static ServiceAccessTokenProvider CreateProvider(
        CountingHandler handler,
        string clientSecret = "configured-secret")
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://auth/") };
        return new ServiceAccessTokenProvider(
            new StubHttpClientFactory(client),
            Options.Create(
                new ServiceAuthenticationOptions
                {
                    ClientId = "service-production-legacy-web",
                    ClientSecret = clientSecret
                }),
            TimeProvider.System,
            NullLogger<ServiceAccessTokenProvider>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("auth", name);
            return client;
        }
    }

    private sealed class CountingHandler(string responseJson) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
