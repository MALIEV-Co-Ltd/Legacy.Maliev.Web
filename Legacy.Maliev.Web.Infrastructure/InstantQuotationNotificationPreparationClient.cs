using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class InstantQuotationNotificationPreparationClient(
    IHttpClientFactory clients, IServiceAccessTokenProvider tokens, TimeProvider timeProvider)
{
    internal TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    internal async Task<InstantQuotationNotificationPreparationResult> PrepareAsync(
        InstantQuotationSessionState session, InstantQuotationOrderQuote quote, InstantQuotationCustomerSubmission customer,
        int reference, IReadOnlyList<InstantQuotationFinalizedFile> files, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = timeProvider.GetUtcNow();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(OperationTimeout);
        try
        {
            if (session.Parts.Count is < 1 or > 32 || files.Count != session.Parts.Count || quote.Parts.Count != session.Parts.Count)
                return new(null, true, true);
            var items = new List<AdditiveQuotationNotificationItem>();
            var expiresAt = started.AddMinutes(45);
            foreach (var part in session.Parts)
            {
                if (!Guid.TryParseExact(part.UploadReference.Value, "D", out var fileId)) return new(null, true, true);
                var matchingFiles = files.Where(file => file.FileId == fileId).ToArray();
                var matchingQuotes = quote.Parts.Where(item => item.PartId == part.PartId).ToArray();
                if (matchingFiles.Length != 1 || matchingQuotes.Length != 1) return new(null, true, true);
                var file = matchingFiles[0];
                var itemQuote = matchingQuotes[0];
                if (file.Sha256 != part.Geometry.Sha256 || itemQuote.Quantity != part.Configuration.Quantity
                    || itemQuote.MaterialKey != part.Configuration.MaterialKey) return new(null, true, true);
                var link = await ReadLinkAsync(file, deadline.Token);
                if (!link.Available || !link.Authorized || link.Url is null) return new(null, link.Available, link.Authorized);
                if (link.ExpiresAt < expiresAt) expiresAt = link.ExpiresAt;
                var material = PricingCatalog.ResolveMaterial(itemQuote.MaterialKey);
                if (material is null) return new(null, true, true);
                items.Add(new(part.DisplayFileName, material.DisplayName, itemQuote.Color,
                    FormattableString.Invariant($"{part.Geometry.DimensionXmm:0.###} x {part.Geometry.DimensionYmm:0.###} x {part.Geometry.DimensionZmm:0.###}"),
                    GeometryWarnings(part.Geometry),
                    itemQuote.Quantity, Convert.ToDecimal(itemQuote.UnitPrice), Convert.ToDecimal(itemQuote.Subtotal),
                    itemQuote.Process, itemQuote.BuildPreference, link.Url, Convert.ToDecimal(itemQuote.PrintTimeMinutesPerUnit),
                    Convert.ToDecimal(itemQuote.PrintTimeMinutesPerUnit * itemQuote.Quantity)));
            }
            if (expiresAt <= timeProvider.GetUtcNow()) return new(null, true, true);
            var plan = AdditiveQuotationNotificationComposer.Compose(new(reference, customer, items,
                Convert.ToDecimal(quote.FinalOrderPrice), quote.LeadTimeMaximumDays,
                quote.ShippingState == ShippingPricingState.DomesticPriced, quote.DestinationCountryCode, quote.LeadTimeMinimumDays));
            return new(new(plan.Customer, plan.Manufacturing, expiresAt), true, true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(null, false, true); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException) { return new(null, false, true); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) { return new(null, true, true); }
    }

    private async Task<(Uri? Url, DateTimeOffset ExpiresAt, bool Available, bool Authorized)> ReadLinkAsync(
        InstantQuotationFinalizedFile file, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file.Bucket) || file.Bucket.Length > 50 || file.Bucket.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(file.ObjectName) || file.ObjectName.Length > 2048 || file.ObjectName.Any(char.IsControl))
            return (null, default, true, true);
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return (null, default, false, false);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"uploads/signedurl?bucket={Uri.EscapeDataString(file.Bucket)}&objectName={Uri.EscapeDataString(file.ObjectName)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var pendingResponse = clients.CreateClient("files").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        HttpResponseMessage? response = null;
        var responseAcquired = false;
        Task? pendingBuffer = null;
        try
        {
            response = await pendingResponse.WaitAsync(cancellationToken);
            responseAcquired = true;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokens.Invalidate(token);
                return (null, default, true, false);
            }
            if (response.StatusCode != HttpStatusCode.OK) return (null, default, (int)response.StatusCode < 500, true);
            if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Headers.Date is not { } serverDate)
                return (null, default, true, true);
            pendingBuffer = response.Content.LoadIntoBufferAsync(65_536, cancellationToken);
            await pendingBuffer.WaitAsync(cancellationToken);
            pendingBuffer = null;
            var value = JsonSerializer.Deserialize<string>(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            // Existing FileService signing lasts 1–168 hours. HTTP Date keeps the conservative bound server-relative.
            var expires = serverDate.AddMinutes(45);
            return Uri.TryCreate(value, UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps && link.UserInfo.Length == 0
                ? (link, expires, true, true) : (null, default, true, true);
        }
        finally
        {
            if (pendingBuffer is not null && response is not null)
            {
                // Close the acquired response immediately even if its content ignores cancellation.
                _ = pendingBuffer.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            if (response is not null) DisposeResponse(response);
            if (!responseAcquired)
                _ = pendingResponse.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully) DisposeResponse(task.Result);
                    else _ = task.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static void DisposeResponse(HttpResponseMessage response)
    {
        try { response.Dispose(); }
        catch (Exception) { }
    }

    private static string? GeometryWarnings(AuthoritativeInstantQuotationGeometry geometry)
    {
        var warnings = new List<string>();
        if (!geometry.TopologyChecked) warnings.Add("Detailed topology not checked");
        if (geometry.NonWatertight) warnings.Add("Non-watertight mesh");
        if (geometry.NonManifold) warnings.Add("Non-manifold edges");
        if (geometry.BodyCount > 1)
            warnings.Add(FormattableString.Invariant($"Multi-body mesh ({geometry.BodyCount} bodies)"));
        var maximumDimension = Math.Max(geometry.DimensionXmm, Math.Max(geometry.DimensionYmm, geometry.DimensionZmm));
        if (maximumDimension is > 0 and < 3) warnings.Add("Dimension below 3 mm");
        if (maximumDimension > 350) warnings.Add("Dimension exceeds 350 mm");
        return warnings.Count == 0 ? null : string.Join("; ", warnings);
    }
}
