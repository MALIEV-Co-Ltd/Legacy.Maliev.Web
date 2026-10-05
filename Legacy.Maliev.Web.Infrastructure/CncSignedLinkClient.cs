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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(client.Timeout);
        HttpResponseMessage? response = null;
        Task<HttpResponseMessage>? pendingResponse = null;
        Task? pendingBuffer = null;
        Uri? resolvedLink = null;
        try
        {
            pendingResponse = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response = await pendingResponse.WaitAsync(deadline.Token);
            pendingResponse = null;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokens.Invalidate(token);
            }

            deadline.Token.ThrowIfCancellationRequested();
            if (response.StatusCode != HttpStatusCode.OK
                || !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            pendingBuffer = response.Content.LoadIntoBufferAsync(MaximumResponseBytes, deadline.Token);
            await pendingBuffer.WaitAsync(deadline.Token);
            pendingBuffer = null;
            string? value = JsonSerializer.Deserialize<string>(
                await response.Content.ReadAsByteArrayAsync(deadline.Token));
            deadline.Token.ThrowIfCancellationRequested();
            resolvedLink = Uri.TryCreate(value, UriKind.Absolute, out var link)
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
        finally
        {
            if (pendingBuffer is not null && response is not null)
            {
                // The buffer can finish after cancellation even when content ignores it.
                // Keep response ownership until that task settles, then dispose once.
                _ = DisposeAfterBufferAsync(pendingBuffer, response);
                response = null;
            }

            try
            {
                response?.Dispose();
            }
            catch (Exception exception) when (IsTransportFailure(exception))
            {
                resolvedLink = null;
            }

            if (pendingResponse is not null)
            {
                _ = DisposeAfterResponseAsync(pendingResponse);
            }
        }

        return resolvedLink;
    }

    private static async Task DisposeAfterBufferAsync(Task buffer, HttpResponseMessage response)
    {
        try
        {
            using (response)
            {
                try
                {
                    await buffer;
                }
                catch (Exception)
                {
                    // Observe the abandoned buffer fault without resuming link resolution.
                }
            }
        }
        catch (Exception)
        {
            // Observe cleanup faults from the abandoned response as well.
        }
    }

    private static async Task DisposeAfterResponseAsync(Task<HttpResponseMessage> pendingResponse)
    {
        try
        {
            using var response = await pendingResponse;
        }
        catch (Exception)
        {
            // Observe a late send fault; a late response is disposed without reading it.
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
