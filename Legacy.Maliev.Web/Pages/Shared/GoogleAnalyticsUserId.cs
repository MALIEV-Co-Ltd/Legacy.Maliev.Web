using Legacy.Maliev.Web.Application;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace Legacy.Maliev.Web.Pages.Shared;

/// <summary>Builds consented GA4 identity from the authenticated, server-owned subject.</summary>
public static class GoogleAnalyticsUserId
{
    /// <summary>Serializes only an opaque subject when both authentication and tracking consent are present.</summary>
    /// <param name="principal">The authenticated request principal.</param>
    /// <param name="canTrack">Whether the request has granted optional tracking consent.</param>
    /// <param name="configurationJson">Safely encoded GA4 configuration, or an empty string.</param>
    /// <returns>Whether a configuration was created.</returns>
    public static bool TryBuildConfiguration(ClaimsPrincipal? principal, bool canTrack, out string configurationJson)
    {
        configurationJson = string.Empty;
        if (!canTrack || principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var subjects = principal.FindAll(ClaimTypes.NameIdentifier).Take(2).ToArray();
        if (subjects.Length != 1 || !IsOpaqueSubject(subjects[0].Value))
        {
            return false;
        }

        var retainedSubjects = principal.FindAll(CustomerIdentityClaims.AnalyticsSubject).Take(2).ToArray();
        if (retainedSubjects.Length > 1
            || (retainedSubjects.Length == 1 && string.IsNullOrWhiteSpace(retainedSubjects[0].Value)))
        {
            return false;
        }
        var subject = retainedSubjects.Length == 1 ? retainedSubjects[0].Value : subjects[0].Value;
        configurationJson = JsonSerializer.Serialize(new { user_id = subject });
        return true;
    }

    private static bool IsOpaqueSubject(string subject)
    {
        // Target customer cookies use this namespace, rather than the source Identity GUID.
        if (subject.StartsWith("customer:", StringComparison.Ordinal))
        {
            var identifier = subject["customer:".Length..];
            return int.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id > 0 && identifier == id.ToString(CultureInfo.InvariantCulture);
        }

        return Guid.TryParseExact(subject, "D", out var identity) && identity != Guid.Empty;
    }
}
