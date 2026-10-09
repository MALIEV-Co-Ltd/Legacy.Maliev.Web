using System.Net.Http.Headers;
using System.Text.Json;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>Uses only the acting member's server-held credential; no service impersonation.</summary>
public sealed class CustomerDocumentClient(HttpClient httpClient) : ICustomerDocumentClient
{
    private const int MaximumBytes = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false };

    public async Task<CustomerDocumentResult<CustomerDocumentVersionReceipt>> UploadAsync(int customerId, Guid? documentId, long? expectedRevision, string kind, string title, Stream content, string fileName, string contentType, string idempotencyKey, string accessToken, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, documentId is null ? $"customers/{customerId}/documents" : $"customers/{customerId}/documents/{documentId:D}/versions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(kind), "Kind");
            multipart.Add(new StringContent(title), "Title");
            multipart.Add(new StringContent("Customer"), "Visibility");
            multipart.Add(new StringContent("[]"), "Associations");
            if (expectedRevision is not null) multipart.Add(new StringContent(expectedRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "ExpectedRevision");
            using var file = new StreamContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipart.Add(file, "files", Path.GetFileName(fileName));
            request.Content = multipart;
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return new(SafeStatus(response), null);
            var value = JsonSerializer.Deserialize<CustomerDocumentVersionReceipt>(await ReadBoundedAsync(response.Content, 256 * 1024, token), Json);
            return value is null || value.CustomerId != customerId || value.DocumentId == Guid.Empty || value.VersionId == Guid.Empty || value.Revision <= 0 || (documentId is not null && value.DocumentId != documentId) ? new(503, null) : new(200, value);
        }
        catch (Exception exception) when (Bounded(exception, token)) { return new(503, null); }
    }

    public Task<CustomerDocumentResult<IReadOnlyList<CustomerDocumentSummary>>> ListAsync(int customerId, string accessToken, CancellationToken token) =>
        ReadJsonAsync<IReadOnlyList<CustomerDocumentSummary>>($"customers/{customerId}/documents", accessToken, token);
    public Task<CustomerDocumentResult<IReadOnlyList<CustomerDocumentVersionSummary>>> VersionsAsync(int customerId, Guid documentId, string accessToken, CancellationToken token) =>
        ReadJsonAsync<IReadOnlyList<CustomerDocumentVersionSummary>>($"customers/{customerId}/documents/{documentId:D}/versions", accessToken, token);

    public Task<CustomerDocumentResult<CustomerDocumentReceipt>> ReceiptAsync(int customerId, Guid documentId, Guid versionId, string accessToken, CancellationToken token) =>
        ReadJsonAsync<CustomerDocumentReceipt>($"customers/{customerId}/documents/{documentId:D}/versions/{versionId:D}/receipt", accessToken, token);

    public async Task<CustomerDocumentResult<byte[]>> DownloadAsync(int customerId, Guid documentId, Guid versionId, string accessToken, CancellationToken token)
    {
        try
        {
            using var response = await SendAsync($"customers/{customerId}/documents/{documentId:D}/versions/{versionId:D}/download", accessToken, token);
            if (!response.IsSuccessStatusCode) return new(SafeStatus(response), null);
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("application/pdf" or "image/png" or "image/jpeg")) return new(503, null);
            var bytes = await ReadBoundedAsync(response.Content, MaximumBytes, token);
            return bytes.Length == 0 ? new(503, null) : new(200, bytes);
        }
        catch (Exception exception) when (Bounded(exception, token)) { return new(503, null); }
    }

    private async Task<CustomerDocumentResult<T>> ReadJsonAsync<T>(string path, string accessToken, CancellationToken token)
    {
        try
        {
            using var response = await SendAsync(path, accessToken, token);
            if (!response.IsSuccessStatusCode) return new(SafeStatus(response), default);
            var bytes = await ReadBoundedAsync(response.Content, 256 * 1024, token);
            var value = JsonSerializer.Deserialize<T>(bytes, Json);
            return value is null ? new(503, default) : new(200, value);
        }
        catch (Exception exception) when (Bounded(exception, token)) { return new(503, default); }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string accessToken, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidDataException("Acting credential unavailable.");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > limit) throw new InvalidDataException();
        await using var source = await content.ReadAsStreamAsync(token);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            if (destination.Length + read > limit) throw new InvalidDataException();
            destination.Write(buffer, 0, read);
        }
        if (content.Headers.ContentLength is { } expected && expected != destination.Length) throw new InvalidDataException();
        return destination.ToArray();
    }

    private static int SafeStatus(HttpResponseMessage response) => (int)response.StatusCode is 400 or 401 or 403 or 404 or 409 or 413 or 415 or 422 ? (int)response.StatusCode : 503;
    private static bool Bounded(Exception exception, CancellationToken token) => exception is HttpRequestException or InvalidDataException or JsonException || exception is OperationCanceledException && !token.IsCancellationRequested;
}
