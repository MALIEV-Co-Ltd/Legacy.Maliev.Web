using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class QuotationFileClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<QuotationFileClient> logger) : IQuotationFileClient
{
    private const string Bucket = "maliev.com";

    public async Task<QuotationFileResult> UploadAndLinkAsync(
        int requestId,
        Guid submissionId,
        IReadOnlyList<QuotationUpload> files,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return new QuotationFileResult(true, true, true, false);
        }

        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Quotation file upload was rejected because service authentication was unavailable.");
            return new QuotationFileResult(false, false, false, false);
        }

        try
        {
            using var form = CreateForm(files);
            var path = $"quotation-request/{requestId}/{submissionId:N}";
            using var uploadRequest = CreateAuthenticatedRequest(
                HttpMethod.Post,
                $"Uploads?bucket={Escape(Bucket)}&path={Escape(path)}",
                token,
                form);
            using var uploadResponse = await clientFactory.CreateClient("files")
                .SendAsync(uploadRequest, cancellationToken);

            if (uploadResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
                return new QuotationFileResult(false, true, false, false);
            }

            if (uploadResponse.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                logger.LogWarning("File service rejected malware in quotation request {RequestId}.", requestId);
                return new QuotationFileResult(false, true, true, true);
            }

            uploadResponse.EnsureSuccessStatusCode();
            var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>(cancellationToken);
            if (uploaded is null || uploaded.Object.Count == 0)
            {
                return new QuotationFileResult(false, true, true, false);
            }

            for (var index = 0; index < uploaded.Object.Count; index++)
            {
                var item = uploaded.Object[index];
                using var linkRequest = CreateAuthenticatedRequest(
                    HttpMethod.Post,
                    $"quotationrequests/{requestId}/files?bucket={Escape(item.Bucket)}&objectName={Escape(item.ObjectName)}",
                    token);
                using var linkResponse = await clientFactory.CreateClient("quotations")
                    .SendAsync(linkRequest, cancellationToken);
                if (!linkResponse.IsSuccessStatusCode)
                {
                    if (linkResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        tokenProvider.Invalidate(token);
                    }

                    await DeleteUnlinkedAsync(uploaded.Object.Skip(index).ToArray(), token, cancellationToken);
                    return new QuotationFileResult(
                        false,
                        linkResponse.StatusCode != HttpStatusCode.ServiceUnavailable,
                        linkResponse.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden,
                        false);
                }
            }

            return new QuotationFileResult(true, true, true, false);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "File or quotation service was unavailable while attaching request files.");
            return new QuotationFileResult(false, false, true, false);
        }
    }

    private async Task DeleteUnlinkedAsync(
        IReadOnlyList<UploadObject> objects,
        string token,
        CancellationToken cancellationToken)
    {
        foreach (var item in objects)
        {
            try
            {
                using var request = CreateAuthenticatedRequest(
                    HttpMethod.Delete,
                    $"Uploads?bucket={Escape(item.Bucket)}&objectName={Escape(item.ObjectName)}",
                    token);
                using var response = await clientFactory.CreateClient("files")
                    .SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Could not compensate unlinked quotation upload {ObjectName}; status {StatusCode}.",
                        item.ObjectName,
                        response.StatusCode);
                }
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken))
            {
                logger.LogWarning(
                    exception,
                    "Could not compensate unlinked quotation upload {ObjectName}.",
                    item.ObjectName);
            }
        }
    }

    private static MultipartFormDataContent CreateForm(IReadOnlyList<QuotationUpload> files)
    {
        var form = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var content = new StreamContent(file.OpenReadStream());
            content.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var mediaType)
                ? mediaType
                : new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", file.FileName);
        }

        return form;
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string uri,
        string token,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record UploadResult(IReadOnlyList<UploadObject> Object);

    private sealed record UploadObject(string Bucket, string ObjectName, Uri Uri);
}
