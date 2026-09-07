using Legacy.Maliev.Web.Application;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Polly;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CncFileFinalizationClient(IHttpClientFactory clients, IServiceAccessTokenProvider tokens) : ICncFileFinalizationClient
{
    private const string SourceBucket = "maliev-instant-quotations";
    private const string DestinationBucket = "maliev-quotation-requests";

    public async Task<CncFileFinalizationResult> FinalizeAsync(CncFileFinalizationRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !TryDestination(request, out var destination))
            return new(CncFileFinalizationOutcome.NotSent);
        string? token;
        HttpClient fileClient;
        HttpClient quotationClient;
        try
        {
            token = await tokens.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return new(CncFileFinalizationOutcome.NotSent);
            fileClient = clients.CreateClient("files");
            quotationClient = clients.CreateClient("quotations");
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return new(CncFileFinalizationOutcome.NotSent);
        }

        using var move = new HttpRequestMessage(HttpMethod.Put,
            $"Uploads?sourceBucket={SourceBucket}&sourceObjectName={Uri.EscapeDataString(request.File.StoragePath)}"
            + $"&destinationBucket={DestinationBucket}&destinationObjectName={Uri.EscapeDataString(destination!)}");
        move.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (cancellationToken.IsCancellationRequested) return new(CncFileFinalizationOutcome.NotSent);
        try
        {
            using var response = await fileClient.SendAsync(move, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Invalidate(token);
            if (response.StatusCode != HttpStatusCode.NoContent) return new(CncFileFinalizationOutcome.MoveUnconfirmed);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            // Storage copy/delete and metadata updates are not one transaction. Never replay or compensate here.
            return new(CncFileFinalizationOutcome.MoveUnconfirmed);
        }

        using var link = new HttpRequestMessage(HttpMethod.Post,
            $"quotationrequests/{request.RequestId}/files?bucket={DestinationBucket}&objectName={Uri.EscapeDataString(destination!)}");
        link.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await quotationClient.SendAsync(link, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Invalidate(token);
            if (response.StatusCode != HttpStatusCode.Created
                || !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                return new(CncFileFinalizationOutcome.MovedLinkUnconfirmed, destination);
            await response.Content.LoadIntoBufferAsync(65536, cancellationToken);
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("Id", out var id) || !id.TryGetInt32(out var fileId) || fileId <= 0
                || !root.TryGetProperty("RequestId", out var parent) || !parent.TryGetInt32(out var parentId) || parentId != request.RequestId
                || !root.TryGetProperty("Bucket", out var bucket) || bucket.GetString() != DestinationBucket
                || !root.TryGetProperty("ObjectName", out var name) || name.GetString() != destination
                || !IsMatchingLocation(response.Headers.Location, link.RequestUri, fileId))
                return new(CncFileFinalizationOutcome.MovedLinkUnconfirmed, destination);
            return new(CncFileFinalizationOutcome.Linked, destination, fileId);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return new(CncFileFinalizationOutcome.MovedLinkUnconfirmed, destination);
        }
    }

    private static bool IsMatchingLocation(Uri? location, Uri? requestUri, int fileId)
    {
        var expectedPath = $"/quotationrequests/files/{fileId}";
        if (location is null) return false;
        if (!location.IsAbsoluteUri) return location.OriginalString == expectedPath;
        // CreatedAtRoute emits an absolute URL. Validate it without following it or trusting another origin.
        return requestUri is { IsAbsoluteUri: true }
            && location.Scheme == requestUri.Scheme
            && location.Authority == requestUri.Authority
            && string.IsNullOrEmpty(location.UserInfo)
            && location.PathAndQuery == expectedPath
            && string.IsNullOrEmpty(location.Fragment);
    }

    private static bool IsTransportFailure(Exception exception) => exception is HttpRequestException or OperationCanceledException
        or InvalidOperationException or IOException or JsonException or ExecutionRejectedException;

    private static bool TryDestination(CncFileFinalizationRequest request, out string? destination)
    {
        destination = null;
        if (request is null || request.File is null || request.RequestId <= 0 || request.SubmissionStartedAtUtc == default
            || !Guid.TryParseExact(request.SessionId, "D", out var session) || session == Guid.Empty
            || request.SessionId != session.ToString("D") || request.File.SessionId != request.SessionId
            || string.IsNullOrWhiteSpace(request.File.StoragePath) || request.File.StoragePath.Length > 1024) return false;
        var path = request.File.StoragePath;
        if (path != path.Trim().ToLowerInvariant() || path.Any(character => char.IsControl(character) || character is '\\' or '?' or '#')) return false;
        var parts = path.Split('/');
        if (parts.Length != 3 || parts[1] != request.SessionId
            || !DateOnly.TryParseExact(parts[0], "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || parts[0] != $"{date.Year}-{date.Month}-{date.Day}") return false;
        var extension = Path.GetExtension(parts[2]);
        if (!(request.File.Role == "model" ? extension is ".step" or ".stp" or ".iges" or ".igs"
            : request.File.Role == "drawing" && extension == ".pdf")
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(parts[2]), "N", out _)) return false;
        var timestamp = request.SubmissionStartedAtUtc.UtcDateTime;
        destination = $"instant-quotation/{timestamp.Year}-{timestamp.Month}-{timestamp.Day}/{request.SessionId}/{parts[2]}";
        return true;
    }
}
