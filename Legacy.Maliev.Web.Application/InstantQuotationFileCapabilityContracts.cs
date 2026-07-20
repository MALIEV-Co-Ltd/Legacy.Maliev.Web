namespace Legacy.Maliev.Web.Application;

internal sealed record InstantQuotationFileCapability(
    Guid SessionId,
    string SessionToken,
    DateTimeOffset ExpiresAt,
    long MaxUploadBytes,
    int MaxFilesPerSession,
    IReadOnlyList<string> SupportedExtensions);

internal interface IInstantQuotationFileCapabilityStore
{
    Task<bool> PutAsync(
        string webSessionId,
        string? ownerIdentity,
        InstantQuotationFileCapability capability,
        CancellationToken cancellationToken);

    Task<InstantQuotationFileCapability?> GetAsync(
        string webSessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken);

    Task<bool> RemoveAsync(
        string webSessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken);
}
