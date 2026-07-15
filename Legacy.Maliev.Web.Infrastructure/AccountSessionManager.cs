using System.Security.Claims;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Legacy.Maliev.Web.Infrastructure;

public enum AccountSignInStatus
{
    Succeeded,
    InvalidCredentials,
    ServiceUnavailable,
}

public interface IAccountSessionManager
{
    Task<AccountSignInStatus> SignInAsync(
        HttpContext context,
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken);

    Task SignOutAsync(HttpContext context, CancellationToken cancellationToken);
    Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken);
}

internal sealed class AccountSessionManager(
    ICustomerAuthenticationClient authenticationClient,
    IAccountSessionStore store,
    TimeProvider timeProvider) : IAccountSessionManager
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);
    internal const string SessionIdClaim = "legacy_session_id";

    public async Task<AccountSignInStatus> SignInAsync(
        HttpContext context,
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        var result = await authenticationClient.LoginAsync(email, password, cancellationToken);
        if (result.Tokens is null)
        {
            return result.ServiceAvailable
                ? AccountSignInStatus.InvalidCredentials
                : AccountSignInStatus.ServiceUnavailable;
        }

        var sessionId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = timeProvider.GetUtcNow();
        var session = new AccountSession(
            email.Trim(),
            result.Tokens.AccessToken,
            result.Tokens.RefreshToken,
            now.AddSeconds(result.Tokens.ExpiresIn),
            result.Tokens.RefreshExpiresAt);
        await store.SetAsync(sessionId, session, cancellationToken);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, session.Email),
                new Claim(ClaimTypes.Email, session.Email),
                new Claim("identity_kind", "customer"),
                new Claim(SessionIdClaim, sessionId),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                AllowRefresh = false,
                ExpiresUtc = session.RefreshExpiresAt,
                IsPersistent = rememberMe,
            });
        return AccountSignInStatus.Succeeded;
    }

    public async Task SignOutAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var sessionId = context.User.FindFirstValue(SessionIdClaim);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var session = await store.GetAsync(sessionId, cancellationToken);
            if (session is not null)
            {
                await authenticationClient.RevokeAsync(session.RefreshToken, cancellationToken);
            }

            await store.RemoveAsync(sessionId, cancellationToken);
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<string?> GetAccessTokenAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var sessionId = context.User.FindFirstValue(SessionIdClaim);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var session = await store.GetAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (session.AccessExpiresAt - RefreshSkew > now)
        {
            return session.AccessToken;
        }

        await using var refreshLock = await store.AcquireRefreshLockAsync(sessionId, cancellationToken);
        if (refreshLock is null)
        {
            return session.AccessExpiresAt > now ? session.AccessToken : null;
        }

        session = await store.GetAsync(sessionId, cancellationToken);
        now = timeProvider.GetUtcNow();
        if (session is null)
        {
            return null;
        }

        if (session.AccessExpiresAt - RefreshSkew > now)
        {
            return session.AccessToken;
        }

        var refreshed = await authenticationClient.RefreshAsync(session.RefreshToken, cancellationToken);
        if (refreshed.Tokens is null)
        {
            if (!refreshed.ServiceAvailable && session.AccessExpiresAt > now)
            {
                return session.AccessToken;
            }

            await store.RemoveAsync(sessionId, cancellationToken);
            return null;
        }

        var rotated = new AccountSession(
            session.Email,
            refreshed.Tokens.AccessToken,
            refreshed.Tokens.RefreshToken,
            now.AddSeconds(refreshed.Tokens.ExpiresIn),
            refreshed.Tokens.RefreshExpiresAt);
        await store.SetAsync(sessionId, rotated, cancellationToken);
        return rotated.AccessToken;
    }
}

internal sealed class AccountCookieEvents(IAccountSessionStore store) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var sessionId = context.Principal?.FindFirstValue(AccountSessionManager.SessionIdClaim);
        if (string.IsNullOrWhiteSpace(sessionId)
            || await store.GetAsync(sessionId, context.HttpContext.RequestAborted) is null)
        {
            context.RejectPrincipal();
        }
    }
}
