namespace Legacy.Maliev.Web.Application;

public sealed record QuotationRequestSubmission(
    string FirstName,
    string LastName,
    string Email,
    string? TelephoneNumber,
    string Country,
    string? CompanyName,
    string? TaxIdentification,
    string Message);

public sealed record QuotationRequestResult(
    int? ReferenceNumber,
    bool ServiceAvailable,
    bool Authorized);

public sealed record QuotationUpload(
    string FileName,
    string ContentType,
    long Length,
    Func<Stream> OpenReadStream);

public sealed record QuotationFileResult(
    bool Completed,
    bool ServiceAvailable,
    bool Authorized,
    bool Rejected);

public interface IQuotationClient
{
    Task<QuotationRequestResult> CreateRequestAsync(
        QuotationRequestSubmission submission,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public interface IQuotationFileClient
{
    Task<QuotationFileResult> UploadAndLinkAsync(
        int requestId,
        Guid submissionId,
        IReadOnlyList<QuotationUpload> files,
        CancellationToken cancellationToken);
}
