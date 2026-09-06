using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>Preserves the generic FileService CNC upload and compensating-delete contracts.</summary>
public sealed class CncFileTransport(IHttpClientFactory clientFactory, IServiceAccessTokenProvider tokenProvider) : ICncFileTransport
{
    private const string Bucket = "maliev-instant-quotations";

    /// <inheritdoc />
    public async Task<CncUploadTransportResult> UploadAsync(string reservedObjectPath, byte[] data, string contentType, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return new(CncUploadTransportOutcome.NotSent);
        if (!ValidPath(reservedObjectPath) || data is null || data.Length == 0 || data.Length > 25 * 1024 * 1024
            || !MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            return new(CncUploadTransportOutcome.NotSent);

        string? token;
        try { token = await tokenProvider.GetAccessTokenAsync(cancellationToken); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        { return new(CncUploadTransportOutcome.NotSent); }
        if (string.IsNullOrWhiteSpace(token)) return new(CncUploadTransportOutcome.NotSent);

        var split = reservedObjectPath.LastIndexOf('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, "Uploads?bucket=" + Bucket + "&path=" + Uri.EscapeDataString(reservedObjectPath[..split]));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(data);
        bytes.Headers.ContentType = mediaType;
        form.Add(bytes, "files", reservedObjectPath[(split + 1)..]);
        request.Content = form;
        HttpClient client;
        try { client = clientFactory.CreateClient("files"); }
        catch (InvalidOperationException) { return new(CncUploadTransportOutcome.NotSent); }
        HttpStatusCode? receivedStatus = null;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            receivedStatus = response.StatusCode;
            if (response.StatusCode == HttpStatusCode.Unauthorized) tokenProvider.Invalidate(token);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType or HttpStatusCode.UnprocessableEntity)
                return new(CncUploadTransportOutcome.Rejected, receivedStatus);
            if (response.StatusCode != HttpStatusCode.Created) return new(CncUploadTransportOutcome.Unknown, receivedStatus);
            // Bound response bytes before parsing: transport replies must contain exactly one object.
            await response.Content.LoadIntoBufferAsync(65536, cancellationToken);
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            if (!json.RootElement.TryGetProperty("Object", out var objects) || objects.ValueKind != JsonValueKind.Array || objects.GetArrayLength() != 1)
                return new(CncUploadTransportOutcome.Unknown, receivedStatus);
            var item = objects[0];
            if (!item.TryGetProperty("Bucket", out var bucket) || bucket.GetString() != Bucket
                || !item.TryGetProperty("ObjectName", out var name) || name.GetString() != reservedObjectPath
                || !item.TryGetProperty("Uri", out var uri) || !Uri.TryCreate(uri.GetString(), UriKind.Absolute, out var location) || location.Scheme != "https")
                return new(CncUploadTransportOutcome.Unknown, receivedStatus);
            return new(CncUploadTransportOutcome.Uploaded, receivedStatus);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or IOException)
        { return new(CncUploadTransportOutcome.Unknown, receivedStatus); }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteReservedObjectAsync(string reservedObjectPath, CancellationToken cancellationToken)
    {
        if (!ValidPath(reservedObjectPath)) return false;
        try
        {
            var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return false;
            using var request = new HttpRequestMessage(HttpMethod.Delete, "Uploads?bucket=" + Bucket + "&objectName=" + Uri.EscapeDataString(reservedObjectPath));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await clientFactory.CreateClient("files").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) tokenProvider.Invalidate(token);
            return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or IOException)
        { return false; }
    }

    private static bool ValidPath(string? path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 1024
        // FileService trims and lowercases final names. Reject a noncanonical reservation
        // before sending so exact-path cleanup cannot target a different object.
        && string.Equals(path, path.Trim().ToLowerInvariant(), StringComparison.Ordinal)
        && path.Contains('/') && path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..")
        && !path.Any(character => char.IsControl(character) || character is '\\' or '?' or '#');
}
