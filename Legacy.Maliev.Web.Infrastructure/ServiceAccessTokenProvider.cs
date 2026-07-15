using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Infrastructure;

public sealed class ServiceAuthenticationOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}

internal sealed class ServiceAccessTokenProvider(
    IHttpClientFactory clientFactory,
    IOptions<ServiceAuthenticationOptions> options,
    TimeProvider timeProvider,
    ILogger<ServiceAccessTokenProvider> logger) : IServiceAccessTokenProvider, IDisposable
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private CachedToken? cachedToken;

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref cachedToken);
        if (IsUsable(current))
        {
            return current!.Token;
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            logger.LogWarning("Web service authentication is not configured.");
            return null;
        }

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            current = cachedToken;
            if (IsUsable(current))
            {
                return current!.Token;
            }

            using var response = await clientFactory.CreateClient("auth").PostAsJsonAsync(
                "auth/v1/service/login",
                new ServiceLoginRequest(settings.ClientId, settings.ClientSecret),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Auth service rejected the Web service identity with status {StatusCode}.",
                    response.StatusCode);
                return null;
            }

            var login = await response.Content.ReadFromJsonAsync<ServiceLoginResponse>(cancellationToken);
            if (login is null || string.IsNullOrWhiteSpace(login.AccessToken) || login.ExpiresIn <= 0)
            {
                logger.LogWarning("Auth service returned an invalid service-login response.");
                return null;
            }

            current = new CachedToken(
                login.AccessToken,
                timeProvider.GetUtcNow().AddSeconds(login.ExpiresIn));
            Volatile.Write(ref cachedToken, current);
            return current.Token;
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Auth service was unavailable while obtaining a service token.");
            return null;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    public void Invalidate(string token)
    {
        var current = Volatile.Read(ref cachedToken);
        if (current is not null && string.Equals(current.Token, token, StringComparison.Ordinal))
        {
            Interlocked.CompareExchange(ref cachedToken, null, current);
        }
    }

    public void Dispose() => tokenLock.Dispose();

    private bool IsUsable(CachedToken? token) =>
        token is not null && token.ExpiresAt - RefreshSkew > timeProvider.GetUtcNow();

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);

    private sealed record ServiceLoginRequest(
        [property: JsonPropertyName("clientId")] string ClientId,
        [property: JsonPropertyName("clientSecret")] string ClientSecret);

    private sealed record ServiceLoginResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn);
}
