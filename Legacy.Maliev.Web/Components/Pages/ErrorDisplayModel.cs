namespace Legacy.Maliev.Web.Components.Pages;

/// <summary>
/// Carries bounded error-page presentation and support correlation metadata without exception details.
/// </summary>
/// <param name="IsNotFound">Whether the page represents a missing resource.</param>
/// <param name="IncidentId">The support incident identifier, or null when no incident was recorded.</param>
/// <param name="OccurredAtUtc">The recorded incident time in UTC, or the default when unavailable.</param>
/// <param name="StatusCode">The HTTP failure status represented by the page, or null when unspecified.</param>
/// <param name="IsPreview">Whether the page is an explicit status preview rather than a recorded failure.</param>
public sealed record ErrorDisplayModel(
    bool IsNotFound,
    string? IncidentId,
    DateTimeOffset OccurredAtUtc = default,
    int? StatusCode = null,
    bool IsPreview = false)
{
    /// <summary>
    /// Indicates whether a nonblank support incident identifier is available for display.
    /// </summary>
    public bool ShowIncidentId => !string.IsNullOrWhiteSpace(IncidentId);
}
