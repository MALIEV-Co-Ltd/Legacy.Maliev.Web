using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Polly;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>Preserves CNC request creation certainty and source PascalCase wire fields.</summary>
public sealed class CncRequestClient(IHttpClientFactory clients, IServiceAccessTokenProvider tokens, TimeProvider clock) : ICncRequestClient
{
    private static readonly JsonSerializerOptions WireJson = new() { PropertyNamingPolicy = null };

    /// <inheritdoc />
    public async Task<CncRequestResult> CreateAsync(CncRequestSubmission submission, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || submission.JourneyId == Guid.Empty)
            return new(CncRequestOutcome.NotSent);

        HttpClient client;
        string? token;
        try
        {
            token = await tokens.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return new(CncRequestOutcome.NotSent);
            client = clients.CreateClient("quotations");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
            or InvalidOperationException or ExecutionRejectedException)
        {
            return new(CncRequestOutcome.NotSent);
        }

        var contact = submission.Contact;
        using var request = new HttpRequestMessage(HttpMethod.Post, "quotationrequests/")
        {
            Content = JsonContent.Create(new
            {
                contact.FirstName,
                contact.LastName,
                contact.Email,
                contact.TelephoneNumber,
                contact.Country,
                contact.CompanyName,
                contact.TaxIdentification,
                contact.Message,
                contact.InternalComment,
                submission.JourneyId,
                Done = (bool?)null,
                CreatedDate = clock.GetUtcNow(),
            }, options: WireJson),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (cancellationToken.IsCancellationRequested) return new(CncRequestOutcome.NotSent);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Invalidate(token);
            // Once POST begins, even a failed response cannot authorize receipt reuse.
            if (response.StatusCode != HttpStatusCode.Created) return new(CncRequestOutcome.Unknown);
            // The source review record allows 256 KiB UTF-8; JSON escaping can expand it sixfold.
            await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, cancellationToken);
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            var value = json.RootElement;
            if (!value.TryGetProperty("Id", out var id) || !id.TryGetInt32(out var number) || number <= 0
                || !value.TryGetProperty("JourneyId", out var journey) || !journey.TryGetGuid(out var guid) || guid != submission.JourneyId
                || (value.TryGetProperty("Done", out var done) && done.ValueKind != JsonValueKind.Null))
                return new(CncRequestOutcome.Unknown);
            return new(CncRequestOutcome.Created, number);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
            or JsonException or InvalidOperationException or IOException or ExecutionRejectedException)
        {
            return new(CncRequestOutcome.Unknown);
        }
    }
}
