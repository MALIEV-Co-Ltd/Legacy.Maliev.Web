namespace Legacy.Maliev.Web.Application;

/// <summary>Identifies the legacy manufacturing order flow selected by the customer.</summary>
public enum CustomerOrderKind
{
    /// <summary>3D-printing order.</summary>
    Additive,

    /// <summary>3D-scanning order.</summary>
    Scanning,

    /// <summary>CNC-machining order.</summary>
    Machining,
}

/// <summary>Provides one upload without coupling the application layer to ASP.NET form files.</summary>
public interface ICustomerOrderUploadFile
{
    /// <summary>Gets the untrusted client file name.</summary>
    string FileName { get; }

    /// <summary>Gets the declared content type.</summary>
    string ContentType { get; }

    /// <summary>Gets the file length in bytes.</summary>
    long Length { get; }

    /// <summary>Opens a fresh readable stream.</summary>
    Stream OpenReadStream();
}

/// <summary>Contains customer-entered order fields; trusted customer identity is supplied separately.</summary>
public sealed record CustomerOrderDraft(
    CustomerOrderKind Kind,
    string Name,
    string? Description,
    int ProcessId,
    int? MaterialId,
    int? SurfaceFinishId,
    int? ColorId,
    int Quantity,
    bool AllowSocialMedia,
    IReadOnlyList<ICustomerOrderUploadFile> Files,
    decimal? UnitPrice = null,
    int? CurrencyId = null,
    int? LeadTime = null,
    string? Comment = null,
    bool AllowCancellation = true);

/// <summary>Identifies one scanned object returned by FileService.</summary>
public sealed record CustomerOrderUploadedObject(string Bucket, string ObjectName);

/// <summary>Represents the OrderService create outcome.</summary>
public sealed record CustomerOrderCreateResult(
    int? OrderId,
    bool ServiceAvailable,
    bool Authorized,
    bool Conflict);

/// <summary>Represents the FileService upload outcome.</summary>
public sealed record CustomerOrderUploadResult(
    IReadOnlyList<CustomerOrderUploadedObject>? Objects,
    bool ServiceAvailable,
    bool Authorized,
    bool Conflict);

/// <summary>Represents the complete customer order submission outcome.</summary>
public sealed record CustomerOrderSubmissionResult(
    int? OrderId,
    bool Succeeded,
    bool Persisted,
    bool ServiceAvailable,
    bool Authorized,
    bool Conflict,
    bool FilesUploaded,
    bool NotificationSent);

/// <summary>Provides the trusted server-to-server order, status, upload, and linking operations.</summary>
public interface ICustomerOrderSubmissionTransport
{
    /// <summary>Creates or replays one order for the server-derived customer identifier.</summary>
    Task<CustomerOrderCreateResult> CreateAsync(
        int trustedCustomerId,
        CustomerOrderDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Adds or replays the initial New status.</summary>
    Task<CustomerOrderOperationResult> AddNewStatusAsync(
        int orderId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Deletes or confirms absence of an order owned by a failed durable fulfillment.</summary>
    Task<bool> DeleteAsync(int orderId, CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>Uploads optional attachments through malware-scanning FileService.</summary>
    Task<CustomerOrderUploadResult> UploadAsync(
        int trustedCustomerId,
        IReadOnlyList<ICustomerOrderUploadFile> files,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Links one scanned object to an owned order without duplicating an existing exact link.</summary>
    Task<CustomerOrderOperationResult> LinkAsync(
        int trustedCustomerId,
        int orderId,
        CustomerOrderUploadedObject uploadedObject,
        CancellationToken cancellationToken);
}

/// <summary>Coordinates a replay-safe member order submission.</summary>
public interface ICustomerOrderSubmissionService
{
    /// <summary>Submits an order using identity derived from the authenticated server-side session.</summary>
    Task<CustomerOrderSubmissionResult> SubmitAsync(
        int trustedCustomerId,
        string trustedCustomerEmail,
        CustomerOrderDraft draft,
        Guid operationId,
        CancellationToken cancellationToken);
}
