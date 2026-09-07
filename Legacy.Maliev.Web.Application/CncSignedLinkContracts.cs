namespace Legacy.Maliev.Web.Application;

/// <summary>Resolves a short-lived HTTPS download link for a finalized CNC request object.</summary>
internal interface ICncSignedLinkClient
{
    Task<Uri?> GetAsync(string objectName, CancellationToken cancellationToken);
}
