namespace Legacy.Maliev.Web.Application;

/// <summary>Determines whether an upload reservation may be released or needs reconciliation.</summary>
public enum CncUploadTransportOutcome
{
    /// <summary>No upload request was sent.</summary>
    NotSent,
    /// <summary>The service definitively rejected the upload.</summary>
    Rejected,
    /// <summary>The service confirmed the exact reserved object.</summary>
    Uploaded,
    /// <summary>Object creation may have occurred and requires reconciliation.</summary>
    Unknown,
}

/// <summary>Server-only result; unknown outcomes require exact-path cleanup before releasing receipts.</summary>
public sealed record CncUploadTransportResult(CncUploadTransportOutcome Outcome);

/// <summary>Generic CNC model and drawing transport using a server-reserved storage path.</summary>
public interface ICncFileTransport
{
    /// <summary>Uploads bytes to the server-reserved object and reports certainty of the result.</summary>
    Task<CncUploadTransportResult> UploadAsync(string reservedObjectPath, byte[] data, string contentType, CancellationToken cancellationToken);
    /// <summary>Confirms cleanup only when the service acknowledges deletion of the exact object.</summary>
    Task<bool> DeleteReservedObjectAsync(string reservedObjectPath, CancellationToken cancellationToken);
}
