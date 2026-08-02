using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerOrderSubmissionTransport(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    TimeProvider timeProvider,
    ILogger<CustomerOrderSubmissionTransport> logger) : ICustomerOrderSubmissionTransport
{
    private const string UploadBucket = "maliev.com";

    public async Task<CustomerOrderCreateResult> CreateAsync(
        int trustedCustomerId,
        CustomerOrderDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false, false);
        }

        var payload = new CreateOrderRequest(
            trustedCustomerId,
            EmployeeId: null,
            draft.Name.Trim(),
            TrimToNull(draft.Description),
            draft.ProcessId,
            draft.MaterialId,
            draft.SurfaceFinishId,
            draft.ColorId,
            draft.Quantity,
            Manufactured: 0,
            UnitPrice: null,
            DiscountPercent: null,
            CurrencyId: null,
            LeadTime: null,
            PromisedDate: null,
            FinishedDate: null,
            Comment: null,
            draft.AllowSocialMedia,
            AllowCancellation: true,
            AllowPayment: false,
            TrackingNumber: null);

        try
        {
            using var request = Authorized(HttpMethod.Post, "orders", token);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            request.Content = JsonContent.Create(payload);
            using var response = await clientFactory.CreateClient("orders").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return CreateFailure(response.StatusCode, token);
            }

            var order = await response.Content.ReadFromJsonAsync<CreatedOrderResponse>(cancellationToken);
            return order?.Id > 0
                ? new(order.Id, true, true, false)
                : new(null, true, true, false);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning("Order service was unavailable while creating a customer order.");
            return new(null, false, true, false);
        }
    }

    public async Task<CustomerOrderOperationResult> AddNewStatusAsync(
        int orderId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, false, false, false);
        }

        try
        {
            using var request = Authorized(HttpMethod.Post, $"orderstatuses/histories/{orderId}/new", token);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            using var response = await clientFactory.CreateClient("orders").SendAsync(request, cancellationToken);
            return Operation(response.StatusCode, token);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning("Order service was unavailable while adding the initial customer order status.");
            return new(false, false, true, false);
        }
    }

    public async Task<CustomerOrderUploadResult> UploadAsync(
        int trustedCustomerId,
        IReadOnlyList<ICustomerOrderUploadFile> files,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false, false);
        }

        try
        {
            using var content = new MultipartFormDataContent();
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(fileName) || file.Length <= 0)
                {
                    return new(null, true, true, false);
                }

                var streamContent = new StreamContent(file.OpenReadStream());
                streamContent.Headers.ContentType = ResolveContentType(file.ContentType);
                content.Add(streamContent, "files", fileName);
            }

            var date = timeProvider.GetUtcNow();
            var path = $"uploads/{trustedCustomerId}/{date.Year}-{date.Month}-{date.Day}";
            var route = $"uploads?bucket={Uri.EscapeDataString(UploadBucket)}&path={Uri.EscapeDataString(path)}";
            using var request = Authorized(HttpMethod.Post, route, token);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            request.Content = content;
            using var response = await clientFactory.CreateClient("files").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var authorized = IsAuthorized(response.StatusCode, token);
                return new(null, (int)response.StatusCode < 500, authorized, response.StatusCode == HttpStatusCode.Conflict);
            }

            var uploaded = await response.Content.ReadFromJsonAsync<UploadResponse>(cancellationToken);
            var objects = uploaded?.Object?
                .Where(item => string.Equals(item.Bucket, UploadBucket, StringComparison.Ordinal)
                    && item.ObjectName.StartsWith($"{path}/", StringComparison.Ordinal)
                    && item.ObjectName.Length > path.Length + 1)
                .Select(item => new CustomerOrderUploadedObject(item.Bucket, item.ObjectName))
                .ToArray();
            return objects is { Length: > 0 }
                && objects.Length == files.Count
                && objects.Distinct().Count() == objects.Length
                ? new(objects, true, true, false)
                : new(null, true, true, false);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning("File service was unavailable while uploading customer order files.");
            return new(null, false, true, false);
        }
    }

    public async Task<CustomerOrderOperationResult> LinkAsync(
        int trustedCustomerId,
        int orderId,
        CustomerOrderUploadedObject uploadedObject,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, false, false, false);
        }

        try
        {
            using var existingRequest = Authorized(
                HttpMethod.Get,
                $"orders/customers/{trustedCustomerId}/{orderId}",
                token);
            using var existingResponse = await clientFactory.CreateClient("orders")
                .SendAsync(existingRequest, cancellationToken);
            if (!existingResponse.IsSuccessStatusCode)
            {
                return Operation(existingResponse.StatusCode, token);
            }

            var details = await existingResponse.Content.ReadFromJsonAsync<OwnedOrderDetails>(cancellationToken);
            if (details?.Order?.CustomerId != trustedCustomerId)
            {
                return new(false, true, false, false);
            }

            if (details.Files?.Any(file =>
                    string.Equals(file.Bucket, uploadedObject.Bucket, StringComparison.Ordinal)
                    && string.Equals(file.ObjectName, uploadedObject.ObjectName, StringComparison.Ordinal)) == true)
            {
                return new(true, true, true, false);
            }

            var route = $"orders/{orderId}/files?bucket={Uri.EscapeDataString(uploadedObject.Bucket)}"
                + $"&objectName={Uri.EscapeDataString(uploadedObject.ObjectName)}";
            using var linkRequest = Authorized(HttpMethod.Post, route, token);
            using var linkResponse = await clientFactory.CreateClient("orders")
                .SendAsync(linkRequest, cancellationToken);
            return Operation(linkResponse.StatusCode, token);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning("Order or file service was unavailable while linking a customer order file.");
            return new(false, false, true, false);
        }
    }

    private CustomerOrderCreateResult CreateFailure(HttpStatusCode statusCode, string token) => new(
        null,
        (int)statusCode < 500,
        IsAuthorized(statusCode, token),
        statusCode == HttpStatusCode.Conflict);

    private CustomerOrderOperationResult Operation(HttpStatusCode statusCode, string token) => new(
        (int)statusCode is >= 200 and < 300,
        (int)statusCode < 500,
        IsAuthorized(statusCode, token),
        statusCode == HttpStatusCode.Conflict);

    private bool IsAuthorized(HttpStatusCode statusCode, string token)
    {
        var authorized = statusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        if (!authorized)
        {
            tokenProvider.Invalidate(token);
        }

        return authorized;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static MediaTypeHeaderValue ResolveContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType)
        && MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            ? parsed
            : new MediaTypeHeaderValue("application/octet-stream");

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or IOException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record CreateOrderRequest(
        int? CustomerId,
        int? EmployeeId,
        string? Name,
        string? Description,
        int ProcessId,
        int? MaterialId,
        int? SurfaceFinishId,
        int? ColorId,
        int Quantity,
        int Manufactured,
        decimal? UnitPrice,
        decimal? DiscountPercent,
        int? CurrencyId,
        int? LeadTime,
        DateTime? PromisedDate,
        DateTime? FinishedDate,
        string? Comment,
        bool AllowSocialMedia,
        bool AllowCancellation,
        bool AllowPayment,
        string? TrackingNumber);

    private sealed record CreatedOrderResponse(int Id);
    private sealed record UploadResponse(IReadOnlyList<UploadedObjectResponse>? Object);
    private sealed record UploadedObjectResponse(string Bucket, string ObjectName);
    private sealed record OwnedOrderDetails(OwnedOrder? Order, IReadOnlyList<OwnedOrderFile>? Files);
    private sealed record OwnedOrder(int Id, int? CustomerId);
    private sealed record OwnedOrderFile(string Bucket, string ObjectName);
}
