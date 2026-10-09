namespace Legacy.Maliev.Web.Application;

public sealed record CustomerDocumentSummary(Guid DocumentId, int CustomerId, string Kind, string Title, string Visibility, long Revision);
public sealed record CustomerDocumentReceipt(Guid DocumentId, Guid VersionId, int CustomerId, string Kind,
    string ContentSha256, int? QuotationId, IReadOnlyList<int> OrderIds, string VerificationStatus,
    string? VerifiedBySubject, DateTimeOffset? VerifiedAtUtc, long Revision);
public sealed record MemberDocumentReceipt(Guid DocumentId, Guid VersionId, string Kind, string ContentSha256,
    string VerificationStatus, long Revision);
public sealed record CustomerDocumentResult<T>(int StatusCode, T? Value);
public sealed record CustomerDocumentVersionReceipt(Guid DocumentId, Guid VersionId, int CustomerId, int VersionNumber, string ContentSha256, long Revision);
public sealed record CustomerDocumentVersionSummary(Guid DocumentId, Guid VersionId, int VersionNumber, string Kind, string ContentSha256, DateTimeOffset CreatedAtUtc, string VerificationStatus, string? VerifiedBySubject, DateTimeOffset? VerifiedAtUtc, long Revision);
public interface ICustomerDocumentClient
{
    Task<CustomerDocumentResult<IReadOnlyList<CustomerDocumentSummary>>> ListAsync(int customerId, string accessToken, CancellationToken token);
    Task<CustomerDocumentResult<IReadOnlyList<CustomerDocumentVersionSummary>>> VersionsAsync(int customerId, Guid documentId, string accessToken, CancellationToken token);
    Task<CustomerDocumentResult<CustomerDocumentReceipt>> ReceiptAsync(int customerId, Guid documentId, Guid versionId, string accessToken, CancellationToken token);
    Task<CustomerDocumentResult<byte[]>> DownloadAsync(int customerId, Guid documentId, Guid versionId, string accessToken, CancellationToken token);
    Task<CustomerDocumentResult<CustomerDocumentVersionReceipt>> UploadAsync(int customerId, Guid? documentId, long? expectedRevision, string kind, string title, Stream content, string fileName, string contentType, string idempotencyKey, string accessToken, CancellationToken token);
}

public static class CustomerDocumentContractGuard
{
    public static bool KnownKind(string? kind) => kind is "Nda" or "Corporate" or "BillingInstruction" or "Shipment" or "Release" or "Acceptance" or "Evidence";
    public static bool Digest(string? digest) => digest is { Length: 64 } && digest.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool MemberSummary(CustomerDocumentSummary value, int customerId) => value.CustomerId == customerId && value.DocumentId != Guid.Empty && value.Revision > 0 && value.Visibility == "Customer" && KnownKind(value.Kind) && !string.IsNullOrWhiteSpace(value.Title) && value.Title.Length <= 250;
    public static bool VerificationEvidence(string? status, string? actor, DateTimeOffset? time) =>
        status is "PendingVerification" or "Verified" or "Rejected"
        && (time is null || time.Value != default && time.Value.Offset == TimeSpan.Zero)
        && (status == "PendingVerification" || !string.IsNullOrWhiteSpace(actor) && time is not null);
    public static bool Version(CustomerDocumentVersionSummary value, Guid documentId, string kind) => value.DocumentId == documentId && value.VersionId != Guid.Empty && value.VersionNumber > 0 && value.Revision > 0 && value.Kind == kind && KnownKind(value.Kind) && Digest(value.ContentSha256) && value.CreatedAtUtc != default && value.CreatedAtUtc.Offset == TimeSpan.Zero && VerificationEvidence(value.VerificationStatus, value.VerifiedBySubject, value.VerifiedAtUtc);
}
