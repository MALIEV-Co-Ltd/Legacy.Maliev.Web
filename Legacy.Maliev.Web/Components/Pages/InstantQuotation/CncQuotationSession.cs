using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Maintains the source protected journey cookie without accepting browser-supplied upload ownership.</summary>
internal sealed class CncQuotationSession(IDataProtectionProvider provider, TimeProvider clock)
{
    private static readonly object SessionKey = new();
    private readonly IDataProtector current = provider.CreateProtector("Maliev.Web.InstantQuotationSession.v2");
    private readonly IDataProtector legacy = provider.CreateProtector("Maliev.Web.InstantQuotationSession.v1");

    internal string GetOrCreate(HttpContext context)
    {
        if (context.Items.TryGetValue(SessionKey, out var cached) && cached is string existing)
        {
            return existing;
        }

        var cookie = context.Request.Cookies["iq_session"];
        var session = Read(cookie, current);
        if (session is null)
        {
            session = Read(cookie, legacy) ?? Guid.NewGuid().ToString();
            context.Response.Cookies.Append("iq_session", current.Protect(session), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Expires = clock.GetUtcNow().AddHours(3),
            });
        }

        context.Items[SessionKey] = session;
        return session;
    }

    private static string? Read(string? value, IDataProtector protector)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            var session = protector.Unprotect(value);
            return Guid.TryParse(session, out _) ? session : null;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
