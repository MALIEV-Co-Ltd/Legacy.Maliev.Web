using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;
using System.Text.Json;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerAuthenticationClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider serviceTokenProvider,
    ILogger<CustomerAuthenticationClient> logger) : ICustomerAuthenticationClient
{
    public async Task<CustomerAuthenticationResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clientFactory.CreateClient("auth").PostAsJsonAsync(
                "auth/v1/login",
                new LoginRequest(email, password, 0),
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new(null, true);
            }

            response.EnsureSuccessStatusCode();
            var tokens = await response.Content.ReadFromJsonAsync<CustomerTokenSet>(cancellationToken);
            return new(tokens, true, ExtractDatabaseId(tokens?.AccessToken));
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Auth service was unavailable during customer login.");
            return new(null, false);
        }
    }

    public async Task<CustomerAuthenticationResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clientFactory.CreateClient("auth").PostAsJsonAsync(
                "auth/v1/refresh",
                new RefreshRequest(refreshToken),
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new(null, true);
            }

            response.EnsureSuccessStatusCode();
            var tokens = await response.Content.ReadFromJsonAsync<CustomerTokenSet>(cancellationToken);
            return new(tokens, true, ExtractDatabaseId(tokens?.AccessToken));
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Auth service was unavailable during customer session refresh.");
            return new(null, false);
        }
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clientFactory.CreateClient("auth").PostAsJsonAsync(
                "auth/v1/revoke",
                new RefreshRequest(refreshToken),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Auth service rejected customer session revocation with status {StatusCode}.", response.StatusCode);
            }
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Auth service was unavailable during customer session revocation.");
        }
    }

    public async Task<CustomerIdentityRegistration> RegisterAsync(
        int databaseId,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await SendServiceRequestAsync(
            HttpMethod.Post,
            "auth/v1/customer-self-service/register",
            new RegisterRequest(databaseId, email, password),
            cancellationToken);
        if (response is null || !response.IsSuccessStatusCode)
        {
            if (response is not null && response.StatusCode != HttpStatusCode.Conflict)
            {
                logger.LogWarning(
                    "Auth service rejected customer identity registration with status {StatusCode}.",
                    response.StatusCode);
            }

            return new(false, null, null, null);
        }

        return await response.Content.ReadFromJsonAsync<CustomerIdentityRegistration>(cancellationToken)
            ?? new(false, null, null, null);
    }

    public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken) =>
        RequestChallengeAsync("email-confirmation/request", email, cancellationToken);

    public Task<bool> CompleteEmailConfirmationAsync(
        string email,
        string token,
        CancellationToken cancellationToken) =>
        CompleteActionAsync(
            "email-confirmation/complete",
            new CompleteActionRequest(email, token),
            cancellationToken);

    public Task<CustomerActionChallenge> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken) =>
        RequestChallengeAsync("password-reset/request", email, cancellationToken);

    public Task<bool> CompletePasswordResetAsync(
        string email,
        string token,
        string password,
        CancellationToken cancellationToken) =>
        CompleteActionAsync(
            "password-reset/complete",
            new CompletePasswordResetRequest(email, token, password),
            cancellationToken);

    public Task<CustomerCredentialOperationResult> ChangeEmailAsync(
        string accessToken,
        string currentPassword,
        string newEmail,
        CancellationToken cancellationToken) =>
        ChangeCredentialAsync(
            "email/change",
            accessToken,
            new ChangeEmailRequest(currentPassword, newEmail),
            expectsChallenge: true,
            cancellationToken);

    public Task<CustomerCredentialOperationResult> ChangePasswordAsync(
        string accessToken,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken) =>
        ChangeCredentialAsync(
            "password/change",
            accessToken,
            new ChangePasswordRequest(currentPassword, newPassword),
            expectsChallenge: false,
            cancellationToken);

    private async Task<CustomerCredentialOperationResult> ChangeCredentialAsync(
        string action,
        string accessToken,
        object content,
        bool expectsChallenge,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"auth/v1/customer-self-service/{action}")
            {
                Content = JsonContent.Create(content),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await clientFactory.CreateClient("auth").SendAsync(
                request,
                cancellationToken);
            var authorized = response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
            if (!response.IsSuccessStatusCode)
            {
                return new(false, (int)response.StatusCode < 500, authorized);
            }

            if (!expectsChallenge)
            {
                return new(true, true, true);
            }

            var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>(cancellationToken);
            return new(challenge?.Accepted == true && challenge.Token is not null, true, true, challenge?.Token);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Auth service was unavailable during a customer credential change.");
            return new(false, false, true);
        }
    }

    private async Task<CustomerActionChallenge> RequestChallengeAsync(
        string action,
        string email,
        CancellationToken cancellationToken)
    {
        using var response = await SendServiceRequestAsync(
            HttpMethod.Post,
            $"auth/v1/customer-self-service/{action}",
            new ActionRequest(email),
            cancellationToken);
        if (response is null)
        {
            return new(false, null, false, false);
        }

        var authorized = response.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden;
        if (!authorized)
        {
            return new(false, null, true, false);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Auth service rejected customer challenge creation with status {StatusCode}.",
                response.StatusCode);
            return new(false, null, (int)response.StatusCode < 500, true);
        }

        var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>(cancellationToken);
        return new(challenge?.Accepted == true, challenge?.Token, true, true);
    }

    private async Task<bool> CompleteActionAsync(
        string action,
        object content,
        CancellationToken cancellationToken)
    {
        using var response = await SendServiceRequestAsync(
            HttpMethod.Post,
            $"auth/v1/customer-self-service/{action}",
            content,
            cancellationToken);
        return response?.IsSuccessStatusCode == true;
    }

    private async Task<HttpResponseMessage?> SendServiceRequestAsync(
        HttpMethod method,
        string path,
        object content,
        CancellationToken cancellationToken)
    {
        var token = await serviceTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Customer identity request was rejected because service authentication was unavailable.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = JsonContent.Create(content),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await clientFactory.CreateClient("auth").SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                serviceTokenProvider.Invalidate(token);
            }

            return response;
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Auth service was unavailable during customer self-service.");
            return null;
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static int? ExtractDatabaseId(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var segments = accessToken.Split('.');
        if (segments.Length != 3)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(segments[1]));
            if (!payload.RootElement.TryGetProperty("legacy_database_id", out var claim))
            {
                return null;
            }

            return claim.ValueKind switch
            {
                JsonValueKind.Number when claim.TryGetInt32(out var number) && number > 0 => number,
                JsonValueKind.String when int.TryParse(
                    claim.GetString(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var number) && number > 0 => number,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private sealed record LoginRequest(string UserName, string Password, int IdentityKind);
    private sealed record RefreshRequest(string RefreshToken);
    private sealed record RegisterRequest(int DatabaseId, string Email, string Password);
    private sealed record ActionRequest(string Email);
    private sealed record CompleteActionRequest(string Email, string Token);
    private sealed record CompletePasswordResetRequest(string Email, string Token, string Password);
    private sealed record ChangeEmailRequest(string CurrentPassword, string NewEmail);
    private sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    private sealed record ChallengeResponse(bool Accepted, string? Token);
}
