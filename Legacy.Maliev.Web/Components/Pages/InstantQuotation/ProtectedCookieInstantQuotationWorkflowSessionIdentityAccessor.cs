using System.Security.Claims;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>
/// Claim used only to bridge the request-established quotation identity into an Interactive Server circuit.
/// </summary>
public static class InstantQuotationSessionIdentityClaim
{
    /// <summary>The private claim type.</summary>
    public const string Type = "urn:maliev:legacy-web:instant-quotation-session";
}

/// <summary>
/// Reads the request-established identity from Blazor's circuit-safe authentication state.
/// </summary>
public sealed class AuthenticationStateInstantQuotationWorkflowSessionIdentityAccessor(
    AuthenticationStateProvider authenticationStateProvider)
    : IInstantQuotationWorkflowSessionIdentityAccessor
{
    /// <inheritdoc />
    public async ValueTask<string?> GetProtectedSessionIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var identity = state.User.FindFirstValue(InstantQuotationSessionIdentityClaim.Type);
        return InstantQuotationSessionIdentityCookie.IsValidIdentity(identity) ? identity : null;
    }

    /// <inheritdoc />
    public async ValueTask SetProtectedSessionIdentityAsync(
        string protectedSessionIdentity,
        CancellationToken cancellationToken)
    {
        var establishedIdentity = await GetProtectedSessionIdentityAsync(cancellationToken);
        if (!string.Equals(establishedIdentity, protectedSessionIdentity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Instant Quotation session identity must be established before the interactive circuit starts.");
        }
    }
}

/// <summary>
/// Establishes and restores the protected quotation identity at the HTTP request boundary.
/// </summary>
public sealed class InstantQuotationSessionIdentityMiddleware(RequestDelegate next)
{
    /// <summary>Processes the Instant Quotation document and Blazor circuit handshake.</summary>
    public async Task InvokeAsync(
        HttpContext context,
        IInstantQuotationSessionStore sessionStore,
        InstantQuotationSessionIdentityCookie identityCookie)
    {
        var isQuotationRoute = string.Equals(
            context.Request.Path.Value,
            "/InstantQuotation/3D-Printing",
            StringComparison.OrdinalIgnoreCase);
        var isCircuitRequest = context.Request.Path.StartsWithSegments(
            "/_blazor",
            StringComparison.OrdinalIgnoreCase);
        if (!isQuotationRoute && !isCircuitRequest)
        {
            await next(context);
            return;
        }

        var ownerIdentity = ResolveOwnerIdentity(context.User);
        var cookieIdentity = identityCookie.TryRead(context);
        var session = cookieIdentity is null
            ? null
            : await sessionStore.GetAsync(cookieIdentity, ownerIdentity, context.RequestAborted);

        if (session is null && isQuotationRoute)
        {
            session = await sessionStore.CreateAsync(
                ownerIdentity,
                new InstantQuotationOrderState([]),
                context.RequestAborted);
            identityCookie.Write(context, session.SessionId, session.CreatedAt.Add(InstantQuotationSessionIdentityCookie.Lifetime));
        }
        else if (session is null && cookieIdentity is not null)
        {
            identityCookie.Delete(context);
        }

        if (session is not null)
        {
            context.User.AddIdentity(new ClaimsIdentity(
                [new Claim(InstantQuotationSessionIdentityClaim.Type, session.SessionId)]));
        }

        await next(context);
    }

    private static string? ResolveOwnerIdentity(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated is not true)
        {
            return null;
        }

        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
    }
}

/// <summary>
/// Protects the opaque server-session identifier stored in the essential host cookie.
/// </summary>
public sealed class InstantQuotationSessionIdentityCookie(
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider,
    ILogger<InstantQuotationSessionIdentityCookie> logger)
{
    /// <summary>The host-only cookie name.</summary>
    public const string CookieName = "__Host-Maliev.InstantQuotation";

    /// <summary>Matches the protected server session's absolute lifetime.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>Isolates the identity-cookie data-protection payload.</summary>
    public const string ProtectorPurpose = "Legacy.Maliev.Web.InstantQuotationWorkflowIdentity.v1";
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    /// <summary>Returns a valid unprotected identity, or null for missing/malformed input.</summary>
    public string? TryRead(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var protectedValue))
        {
            return null;
        }

        try
        {
            var identity = protector.Unprotect(protectedValue);
            return IsValidIdentity(identity) ? identity : null;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            logger.LogWarning("Rejected an invalid protected Instant Quotation session cookie.");
            return null;
        }
    }

    /// <summary>Writes a protected cookie without extending the server session's absolute expiry.</summary>
    public void Write(HttpContext context, string identity, DateTimeOffset absoluteExpiration)
    {
        if (!IsValidIdentity(identity))
        {
            throw new ArgumentException("The Instant Quotation session identity is invalid.", nameof(identity));
        }

        var remaining = absoluteExpiration - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            Delete(context);
            return;
        }

        context.Response.Cookies.Append(
            CookieName,
            protector.Protect(identity),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = remaining,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = true,
            });
    }

    /// <summary>Expires an invalid or orphaned identity cookie when headers remain writable.</summary>
    public void Delete(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Cookies.Delete(
            CookieName,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = true,
            });
    }

    internal static bool IsValidIdentity(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
