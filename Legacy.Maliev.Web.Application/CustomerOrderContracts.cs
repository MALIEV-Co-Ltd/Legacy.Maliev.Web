namespace Legacy.Maliev.Web.Application;

public sealed record CustomerOrder(
    int Id,
    int? CustomerId,
    string? Name,
    string? Description,
    int ProcessId,
    int Quantity,
    int Manufactured,
    int? Remaining,
    decimal? UnitPrice,
    decimal? DiscountPercent,
    decimal? Subtotal,
    int? LeadTime,
    DateTime? PromisedDate,
    DateTime? FinishedDate,
    string? Comment,
    bool AllowCancellation,
    bool AllowPayment,
    string? TrackingNumber,
    DateTime? CreatedDate,
    DateTime? ModifiedDate)
{
    public int? EmployeeId { get; init; }

    public int? MaterialId { get; init; }

    public int? SurfaceFinishId { get; init; }

    public int? ColorId { get; init; }

    public int? CurrencyId { get; init; }

    public int? Turnaround { get; init; }

    public bool AllowSocialMedia { get; init; }
}

public sealed record CustomerOrderProcess(int Id, int CategoryId, string Name);

public sealed record CustomerOrderStatus(
    int Id,
    int OrderId,
    int OrderStatusId,
    string? Name,
    string? Description,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record CustomerOrderFile(
    int Id,
    int OrderId,
    string Bucket,
    string ObjectName,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record CustomerOrderDetails(
    CustomerOrder Order,
    CustomerOrderProcess? Process,
    IReadOnlyList<CustomerOrderStatus> History,
    IReadOnlyList<CustomerOrderFile> Files);

public sealed record CustomerOrderPage(
    IReadOnlyList<CustomerOrder> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords);

public sealed record CustomerOrderListResult(
    CustomerOrderPage? Page,
    bool ServiceAvailable,
    bool Authorized);

public sealed record CustomerOrderDetailsResult(
    CustomerOrderDetails? Details,
    bool ServiceAvailable,
    bool Authorized);

public sealed record CustomerOrderOperationResult(
    bool Succeeded,
    bool ServiceAvailable,
    bool Authorized,
    bool Conflict);

public interface ICustomerOrderClient
{
    Task<CustomerOrderListResult> ListAsync(
        int customerId,
        string? sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken);

    Task<CustomerOrderDetailsResult> GetAsync(
        int customerId,
        int orderId,
        CancellationToken cancellationToken);

    Task<CustomerOrderOperationResult> CancelAsync(
        int customerId,
        int orderId,
        CancellationToken cancellationToken);
}
