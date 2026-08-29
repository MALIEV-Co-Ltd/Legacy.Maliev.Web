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
    public async Task Login_ExtractsCustomerDatabaseIdAndPasswordCapabilityFromAuthIssuedAccessToken()
    {
        var accessToken = Jwt(new { legacy_database_id = "42", has_password = false });
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            $$"""{"accessToken":"{{accessToken}}","refreshToken":"refresh","tokenType":"Bearer","expiresIn":900,"refreshExpiresAt":"2026-07-16T00:00:00Z"}"""));
        var client = CreateClient(handler);

        var result = await client.LoginAsync("customer@example.com", "correct-password", default);

        Assert.Equal(42, result.DatabaseId);
        Assert.False(result.HasPassword);
    }

    [Theory]
    [InlineData("confirm_email")]
    [InlineData("set_initial_password")]
    public async Task Login_ActionRequired_MapsOnlyOpaqueActionFromConflict(string action)
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.Conflict,
            $$"""{"action":"{{action}}","token":"opaque-action-token"}"""));
        var client = CreateClient(handler);

        var result = await client.LoginAsync("customer@example.com", "validated-password", default);

        Assert.Null(result.Tokens);
        Assert.Equal(action, result.RequiredAction?.Action);
        Assert.Equal("opaque-action-token", result.RequiredAction?.Token);
        Assert.DoesNotContain("opaque-action-token", handler.RequestUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown_action", "opaque-action-token")]
    [InlineData("confirm_email", "")]
    public async Task Login_InvalidRequiredActionFailsClosed(string action, string token)
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.Conflict,
            $$"""{"action":"{{action}}","token":"{{token}}"}"""));
        var client = CreateClient(handler);

        var result = await client.LoginAsync("customer@example.com", "validated-password", default);

        Assert.Null(result.Tokens);
        Assert.Null(result.RequiredAction);
        Assert.True(result.ServiceAvailable);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"accessToken\":\"\",\"refreshToken\":\"refresh\",\"tokenType\":\"Bearer\",\"expiresIn\":900,\"refreshExpiresAt\":\"2026-07-16T00:00:00Z\"}")]
    public async Task Login_MalformedOrIncompleteSuccessFailsClosed(string body)
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.LoginAsync("customer@example.com", "validated-password", default);

        Assert.Null(result.Tokens);
        Assert.Null(result.DatabaseId);
        Assert.False(result.ServiceAvailable);
    }

    [Fact]
    public async Task Refresh_EmptyTokenSetFailsClosed()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"accessToken":"","refreshToken":"","tokenType":"Bearer","expiresIn":0,"refreshExpiresAt":"0001-01-01T00:00:00Z"}"""));
        var client = CreateClient(handler);

        var result = await client.RefreshAsync("opaque-refresh", default);

        Assert.Null(result.Tokens);
        Assert.False(result.ServiceAvailable);
    }

    [Fact]
    public async Task RecoverEmailConfirmation_UsesServiceBearerAndJsonOnlyRecoveryGrant()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"accepted":true,"token":"fresh-confirmation-token"}"""));
        var client = CreateClient(handler);

        var result = await client.RecoverEmailConfirmationAsync(
            "customer@example.com",
            "opaque-recovery-token",
            default);

        Assert.Equal("fresh-confirmation-token", result.Token);
        Assert.Equal("Bearer service-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/email-confirmation/recover", handler.RequestUri);
        Assert.DoesNotContain("opaque-recovery-token", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"token\":\"opaque-recovery-token\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteInitialPassword_UsesServiceBearerAndKeepsCredentialOutOfRoute()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler);

        var result = await client.CompleteInitialPasswordAsync(
            "customer@example.com",
            "opaque-setup-token",
            "customer-owned-password",
            default);

        Assert.True(result);
        Assert.Equal("Bearer service-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/initial-password/complete", handler.RequestUri);
        Assert.DoesNotContain("customer-owned-password", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"customer-owned-password\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_UsesServiceBearerAndJsonOnlyPassword()
    {
        var handler = new RecordingHandler(request => Json(
            HttpStatusCode.Created,
            """{"succeeded":true,"identityId":"identity-1","databaseId":42,"email":"customer@example.com","created":true}"""));
        var client = CreateClient(handler);

        var result = await client.RegisterAsync(42, "customer@example.com", "correct-password", default);

        Assert.True(result.Succeeded);
        Assert.True(result.Created);
        Assert.Equal("Bearer service-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/register", handler.RequestUri);
        Assert.DoesNotContain("correct-password", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"databaseId\":42", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveRegistration_UsesServiceBearerAndJsonOnlyPassword()
    {
        var handler = new RecordingHandler(request => Json(
            HttpStatusCode.OK,
            """{"succeeded":true,"identityId":"identity-1","databaseId":42,"email":"customer@example.com","created":false}"""));
        var client = CreateClient(handler);

        var result = await client.ResolveRegistrationAsync(
            42,
            "customer@example.com",
            "new-temporary-password",
            default);

        Assert.True(result.Succeeded);
        Assert.Equal("Bearer service-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/register/resolve", handler.RequestUri);
        Assert.Contains("\"databaseId\":42", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("new-temporary-password", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"new-temporary-password\"", handler.Body, StringComparison.Ordinal);
        Assert.False(result.Created);
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

    [Fact]
    public async Task Register_MalformedSuccessReturnsSafeFailureForSagaCompensation()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created, "not-json"));
        var client = CreateClient(handler);

        var result = await client.RegisterAsync(
            42,
            "customer@example.com",
            "correct-password",
            default);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RecoverEmailConfirmation_MalformedSuccessFailsClosed()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "not-json"));
        var client = CreateClient(handler);

        var result = await client.RecoverEmailConfirmationAsync(
            "customer@example.com",
            "opaque-recovery-token",
            default);

        Assert.False(result.Accepted);
        Assert.False(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task ChangeEmail_UsesCustomerBearerAndJsonOnlyCredentials()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"accepted":true,"token":"confirmation-token"}"""));
        var client = CreateClient(handler);

        var result = await client.ChangeEmailAsync(
            "customer-access-token",
            "current-password",
            "new@example.com",
            default);

        Assert.True(result.Succeeded);
        Assert.Equal("confirmation-token", result.Token);
        Assert.Equal("Bearer customer-access-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/email/change", handler.RequestUri);
        Assert.DoesNotContain("current-password", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"currentPassword\":\"current-password\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"newEmail\":\"new@example.com\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePassword_UsesCustomerBearerAndReturnsSafeRejectedResult()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"Credential change failed"}"""));
        var client = CreateClient(handler);

        var result = await client.ChangePasswordAsync(
            "customer-access-token",
            "wrong-password",
            "new-password",
            default);

        Assert.False(result.Succeeded);
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.Equal("Bearer customer-access-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/password/change", handler.RequestUri);
        Assert.Contains("\"newPassword\":\"new-password\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePassword_UsesCustomerBearerAndMapsConflictWithoutCredentialLeak()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        var client = CreateClient(handler);

        var result = await client.CreatePasswordAsync(
            "customer-access-token",
            "new-password",
            default);

        Assert.False(result.Succeeded);
        Assert.True(result.AlreadyExists);
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.Equal("Bearer customer-access-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/password/create", handler.RequestUri);
        Assert.DoesNotContain("new-password", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"newPassword\":\"new-password\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateEmailChange_UsesServiceBearerAndReturnsBoundIdentityState()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"databaseId":42,"currentEmail":"old@example.com","newEmail":"new@example.com","completed":false}"""));
        var client = CreateClient(handler);

        var result = await client.ValidateEmailChangeAsync("new@example.com", "opaque-token", default);

        Assert.True(result.Valid);
        Assert.Equal(42, result.CustomerId);
        Assert.Equal("old@example.com", result.CurrentEmail);
        Assert.Equal("new@example.com", result.NewEmail);
        Assert.False(result.Completed);
        Assert.Equal("Bearer service-token", handler.Authorization);
        Assert.Equal("auth/v1/customer-self-service/email-change/validate", handler.RequestUri);
        Assert.DoesNotContain("opaque-token", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"token\":\"opaque-token\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteEmailChange_MapsInvalidResponseWithoutLeakingTokenInRoute()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = CreateClient(handler);

        var result = await client.CompleteEmailChangeAsync("new@example.com", "opaque-token", default);

        Assert.False(result.Succeeded);
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.Equal("auth/v1/customer-self-service/email-change/complete", handler.RequestUri);
        Assert.DoesNotContain("opaque-token", handler.RequestUri, StringComparison.Ordinal);
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
