namespace Legacy.Maliev.Web.Application;

/// <summary>Server-only coordinates obtained from a validated and atomically claimed CNC receipt.</summary>
internal sealed record CncClaimedFileCoordinates(string SessionId, string StoragePath, string Role);

/// <summary>Finalization for an already persisted engineering-review request. Never bind from browser JSON.</summary>
internal sealed record CncFileFinalizationRequest(int RequestId, string SessionId, DateTimeOffset SubmissionStartedAtUtc, CncClaimedFileCoordinates File);

internal enum CncFileFinalizationOutcome
{
    NotSent,
    MoveUnconfirmed,
    MovedLinkUnconfirmed,
    Linked,
}

/// <summary>After either write starts, failure is terminal for the existing request; never restore upload claims.</summary>
internal sealed record CncFileFinalizationResult(CncFileFinalizationOutcome Outcome, string? DestinationObjectName = null, int? RequestFileId = null);

internal interface ICncFileFinalizationClient
{
    Task<CncFileFinalizationResult> FinalizeAsync(CncFileFinalizationRequest request, CancellationToken cancellationToken);
}
