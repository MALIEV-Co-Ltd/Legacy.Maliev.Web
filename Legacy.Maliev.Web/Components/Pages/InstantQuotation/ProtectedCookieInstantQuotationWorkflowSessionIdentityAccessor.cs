using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>
/// Keeps the opaque Instant Quotation session identity in a protected, HTTP-only host cookie.
/// </summary>
public sealed class ProtectedCookieInstantQuotationWorkflowSessionIdentityAccessor(
    IHttpContextAccessor httpContextAccessor,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ProtectedCookieInstantQuotationWorkflowSessionIdentityAccessor> logger)
    : IInstantQuotationWorkflowSessionIdentityAccessor
{
    internal const string CookieName = "__Host-Maliev.InstantQuotation";
    internal const string ProtectorPurpose = "Legacy.Maliev.Web.InstantQuotationWorkflowIdentity.v1";
    internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    /// <inheritdoc />
    public ValueTask<string?> GetProtectedSessionIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = httpContextAccessor.HttpContext;
        if (context is null || !context.Request.Cookies.TryGetValue(CookieName, out var protectedValue))
        {
            return ValueTask.FromResult<string?>(null);
        }

        try
        {
            var identity = protector.Unprotect(protectedValue);
            return ValueTask.FromResult(IsValidIdentity(identity) ? identity : null);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Rejected an invalid protected Instant Quotation session cookie.");
            return ValueTask.FromResult<string?>(null);
        }
    }

    /// <inheritdoc />
    public ValueTask SetProtectedSessionIdentityAsync(
        string protectedSessionIdentity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSessionIdentity);
        if (!IsValidIdentity(protectedSessionIdentity))
        {
            throw new ArgumentException("The protected session identity is invalid.", nameof(protectedSessionIdentity));
        }

        var context = httpContextAccessor.HttpContext;
        if (context is null || context.Response.HasStarted)
        {
            logger.LogDebug("The Instant Quotation session cookie could not be updated after the response started.");
            return ValueTask.CompletedTask;
        }

        context.Response.Cookies.Append(
            CookieName,
            protector.Protect(protectedSessionIdentity),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = Lifetime,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = true,
            });
        return ValueTask.CompletedTask;
    }

    private static bool IsValidIdentity(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}
