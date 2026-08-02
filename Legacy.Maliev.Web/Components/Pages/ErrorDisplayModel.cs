namespace Legacy.Maliev.Web.Components.Pages;

public sealed record ErrorDisplayModel(
    bool IsNotFound,
    string? IncidentId,
    DateTimeOffset OccurredAtUtc = default,
    int? StatusCode = null);
