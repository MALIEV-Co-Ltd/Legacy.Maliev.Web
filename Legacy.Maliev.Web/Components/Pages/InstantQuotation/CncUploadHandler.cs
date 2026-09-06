using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Processes admitted dedicated CNC uploads using reserved receipt ownership.</summary>
internal sealed class CncUploadHandler(
    CncProtectedUploadBindings bindings,
    CncQuotationSession sessions,
    ICncFileTransport files,
    IAntiforgery antiforgery,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<CncUploadHandler> logger,
    ICncUploadReceiptStore? receipts = null)
{
    internal async Task<IResult> HandleAsync(HttpContext context)
    {
        if (!CncQuotationAvailability.IsAvailable(environment.IsDevelopment(),
            configuration.GetValue<bool>("CncQuotation:Enabled"), configuration["CncQuotation:ApprovedCommercialRulesVersion"],
            receipts is not null, receipts?.IsSharedDistributedAtomic == true)) return Results.NotFound();

        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(); }

        if (!CncUploadAdmissionPolicy.TryGetValidatedRole(context, out var admittedRole))
        {
            return Failure("The CNC upload form role is missing, ambiguous, or does not match its admission. Refresh the page and try again.");
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length <= 0 || string.IsNullOrWhiteSpace(file.FileName))
            return Failure("Select a non-empty file to upload.");
        var role = Single(form, "uploadRole");
        if (role is not ("model" or "drawing") || !string.Equals(role, admittedRole, StringComparison.Ordinal))
            return Failure("The CNC upload form role is missing, ambiguous, or does not match its admission. Refresh the page and try again.");

        var session = sessions.GetOrCreate(context);
        var item = Single(form, "itemId");
        if (!bindings.TryValidateForm(Single(form, "quotationFormToken"), session, out var quotationForm)
            || quotationForm is null || !CncProtectedUploadBindings.IsValidItemId(item))
            return Failure("This upload does not belong to the active quotation item. Refresh the page and try again.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!(role == "model" ? extension is ".step" or ".stp" or ".iges" or ".igs" : extension == ".pdf"))
            return Failure("CNC models must be STEP/STP or IGES/IGS files; technical drawings must be PDF files.");
        var limit = role == "drawing" ? CncUploadAdmissionPolicy.MaximumDrawingFileSizeBytes : CncUploadAdmissionPolicy.MaximumModelFileSizeBytes;
        if (file.Length > limit || file.Length > int.MaxValue)
            return Failure($"This {role} file exceeds the {limit / (1024 * 1024)} MB CNC upload limit.");

        var data = new byte[(int)file.Length];
        using (var stream = file.OpenReadStream())
        {
            var offset = 0;
            while (offset < data.Length)
            {
                var read = await stream.ReadAsync(data.AsMemory(offset), context.RequestAborted);
                if (read == 0) return Failure("The selected CNC file ended before its declared size and was not accepted.");
                offset += read;
            }
        }

        if (!ValidSignature(extension, role, data))
            return Failure(role == "drawing" ? "The selected file is not a valid PDF technical drawing." : "The selected file content is not a valid STEP or IGES solid CAD document.");

        var now = clock.GetUtcNow();
        var path = $"{now.Year}-{now.Month}-{now.Day}/{session}/{Guid.NewGuid():N}{extension}";
        var token = bindings.CreateReceiptToken(session, quotationForm.FormId, item!, role, file.FileName, path);
        CncUploadReceiptReservation? reservation;
        try
        {
            if (!receipts!.TryReserve(new CncUploadReceiptState(quotationForm.FormId, session, item!, role, token,
                now.Add(CncProtectedUploadBindings.Lifetime)), now, 40, out reservation) || reservation is null)
                return Failure("This quotation already has the maximum number of active uploads, or this item is already uploading. Remove unused parts or try again.");
        }
        catch (Exception exception)
        {
            logger.LogError("CNC upload capacity reservation failed before object creation. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Failure("We could not reserve this CNC upload. No file was uploaded; please try again.");
        }

        CncUploadTransportResult result;
        try
        {
            var suppliedType = file.ContentType == "simplify3d_stl" ? "simplify3d_stl/stl" : file.ContentType;
            var contentType = MediaTypeHeaderValue.TryParse(suppliedType, out var parsed) ? parsed.ToString() : "application/octet-stream";
            result = await files.UploadAsync(path, data, contentType, context.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError("CNC object upload result was ambiguous. ExceptionType={ExceptionType}", exception.GetType().Name);
            return await ReconcileAsync(path, reservation);
        }

        if (result.Outcome == CncUploadTransportOutcome.NotSent)
        {
            receipts!.Rollback(reservation, clock.GetUtcNow());
            return Failure("We could not prepare this upload. No file was uploaded; please try again.");
        }
        if (result.Outcome == CncUploadTransportOutcome.Rejected)
        {
            receipts!.Rollback(reservation, clock.GetUtcNow());
            return Failure($"The file could not be uploaded ({result.StatusCode}). Please try again.");
        }
        if (result.Outcome != CncUploadTransportOutcome.Uploaded) return await ReconcileAsync(path, reservation, result.StatusCode);

        receipts!.Finalize(reservation, clock.GetUtcNow());
        return Results.Json(new { success = true, path, receipt = token });
    }

    private async Task<IResult> ReconcileAsync(string path, CncUploadReceiptReservation reservation, System.Net.HttpStatusCode? statusCode = null)
    {
        // A disconnected browser must not cancel the bounded compensating cleanup attempt.
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (await files.DeleteReservedObjectAsync(path, cleanupTimeout.Token))
            {
                receipts!.Rollback(reservation, clock.GetUtcNow());
                return Failure($"The upload result could not be confirmed{(statusCode.HasValue ? $" ({statusCode})" : "")}, but the reserved object was removed. You can safely try this upload again.");
            }
        }
        catch (Exception exception)
        {
            logger.LogError("CNC compensating delete was ambiguous; reservation remains locked. ExceptionType={ExceptionType}", exception.GetType().Name);
        }
        return Failure($"The upload result and cleanup could not be confirmed{(statusCode.HasValue ? $" ({statusCode})" : "")}. This item remains locked to prevent a duplicate object. Please contact support and do not retry this item.");
    }

    private static IResult Failure(string message) => Results.Json(new { success = false, message });
    private static string? Single(IFormCollection form, string key) => form[key].Count == 1 ? form[key][0] : null;

    private static bool ValidSignature(string extension, string role, byte[] data)
    {
        try
        {
            return role == "drawing" ? CncFileAdmissionValidator.IsValidPdf(data)
                : extension is ".step" or ".stp" ? CncFileAdmissionValidator.HasValidStepEnvelope(data)
                : CncFileAdmissionValidator.IsValidIges(data);
        }
        catch (Exception exception) when (exception is RegexMatchTimeoutException or FormatException or OverflowException
            or ArgumentException or InvalidDataException or IndexOutOfRangeException)
        {
            return false;
        }
    }
}
