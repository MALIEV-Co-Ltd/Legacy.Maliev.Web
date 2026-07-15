using System.Net;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class AccountClientTests
{
    [Fact]
    public async Task Login_SendsCredentialsOnlyInJsonBody()
    {
        var handler = new RecordingHandler(request => Json(
            HttpStatusCode.OK,
            """{"accessToken":"access","refreshToken":"refresh","tokenType":"Bearer","expiresIn":900,"refreshExpiresAt":"2026-07-16T00:00:00Z"}"""));
        var client = CreateClient(handler);

        var result = await client.LoginAsync("customer@example.com", "correct-password", default);

        Assert.NotNull(result.Tokens);
        Assert.Equal("auth/v1/login", handler.RequestUri);
        Assert.DoesNotContain("customer@example.com", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"userName\":\"customer@example.com\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"correct-password\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"identityKind\":0", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_ExtractsPositiveCustomerDatabaseIdFromAuthIssuedAccessToken()
    {
        var accessToken = Jwt(new { legacy_database_id = "42" });
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            $$"""{"accessToken":"{{accessToken}}","refreshToken":"refresh","tokenType":"Bearer","expiresIn":900,"refreshExpiresAt":"2026-07-16T00:00:00Z"}"""));
        var client = CreateClient(handler);

        var result = await client.LoginAsync("customer@example.com", "correct-password", default);

        Assert.Equal(42, result.DatabaseId);
    }

    [Fact]
    public async Task Register_UsesServiceBearerAndJsonOnlyPassword()
    {
        var handler = new RecordingHandler(request => Json(
            HttpStatusCode.Created,
            """{"succeeded":true,"identityId":"identity-1","databaseId":42,"email":"customer@example.com"}"""));
        var client = CreateClient(handler);

        var result = await client.RegisterAsync(42, "customer@example.com", "correct-password", default);

        Assert.True(result.Succeeded);
        Assert.Equal("Bearer service-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/register", handler.RequestUri);
        Assert.DoesNotContain("correct-password", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"databaseId\":42", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_UpstreamFailureReturnsSafeFailureForSagaCompensation()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"temporary failure"}"""));
        var client = CreateClient(handler);

        var result = await client.RegisterAsync(
            42,
            "customer@example.com",
            "correct-password",
            default);

        Assert.False(result.Succeeded);
    }

    private static CustomerAuthenticationClient CreateClient(RecordingHandler handler) => new(
        new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://auth.test/") }),
        new StubServiceTokenProvider(),
        NullLogger<CustomerAuthenticationClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static string Jwt(object payload) =>
        $"e30.{WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload))}.signature";

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public string Authorization { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;
        public string RequestUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestUri = request.RequestUri?.PathAndQuery.TrimStart('/') ?? string.Empty;
            return response(request);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("service-token");

        public void Invalidate(string token)
        {
        }
    }
}
