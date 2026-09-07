using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Polly;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CncSignedLinkClient(
    IHttpClientFactory clients,
    IServiceAccessTokenProvider tokens) : ICncSignedLinkClient
{
    private const string Bucket = "maliev-quotation-requests";
    private const int MaximumResponseBytes = 65_536;

    public async Task<Uri?> GetAsync(string objectName, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !IsFinalizedObjectName(objectName))
        {
            return null;
        }

        string? token;
        HttpClient client;
        try
        {
            token = await tokens.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            client = clients.CreateClient("files");
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"uploads/signedurl?bucket={Bucket}&objectName={Uri.EscapeDataString(objectName)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokens.Invalidate(token);
            }

            if (response.StatusCode != HttpStatusCode.OK
                || !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
            string? value = JsonSerializer.Deserialize<string>(
                await response.Content.ReadAsByteArrayAsync(cancellationToken));
            return Uri.TryCreate(value, UriKind.Absolute, out var link)
                && link.Scheme == Uri.UriSchemeHttps
                && !string.IsNullOrWhiteSpace(link.Host)
                && string.IsNullOrEmpty(link.UserInfo)
                ? link
                : null;
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return null;
        }
    }

    private static bool IsFinalizedObjectName(string? objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName) || objectName.Length > 1_024
            || objectName != objectName.Trim().ToLowerInvariant()
            || objectName.Any(character => char.IsControl(character) || character is '\\' or '?' or '#'))
        {
            return false;
        }

        string[] parts = objectName.Split('/');
        if (parts.Length != 4 || parts[0] != "instant-quotation"
            || !DateOnly.TryParseExact(parts[1], "yyyy-M-d", out var date)
            || parts[1] != $"{date.Year}-{date.Month}-{date.Day}"
            || !Guid.TryParseExact(parts[2], "D", out var session) || session == Guid.Empty
            || parts[2] != session.ToString("D"))
        {
            return false;
        }

        string extension = Path.GetExtension(parts[3]);
        return extension is ".step" or ".stp" or ".iges" or ".igs" or ".pdf"
            && Guid.TryParseExact(Path.GetFileNameWithoutExtension(parts[3]), "N", out _);
    }

    private static bool IsTransportFailure(Exception exception) => exception is HttpRequestException
        or OperationCanceledException or InvalidOperationException or IOException or JsonException
        or ExecutionRejectedException;
}
