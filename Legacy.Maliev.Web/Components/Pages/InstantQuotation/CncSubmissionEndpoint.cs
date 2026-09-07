using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Preserves the source CNC submit handler's retry-versus-terminal boundary.</summary>
internal sealed class CncSubmissionEndpoint(
    CncQuotationSession sessions,
    CncProtectedUploadBindings bindings,
    CncAuthenticatedProfileLoader profiles,
    CncSubmissionPersistenceCoordinator persistence,
    IAntiforgery antiforgery,
    ITempDataDictionaryFactory tempDataFactory,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<CncSubmissionEndpoint> logger,
    ICncUploadReceiptStore? receipts = null,
    CncReceiptClaimCoordinator? claims = null)
{
    internal async Task<IResult> HandleAsync(HttpContext context)
    {
        if (!CncQuotationAvailability.IsAvailable(
                environment.IsDevelopment(),
                configuration.GetValue<bool>("CncQuotation:Enabled"),
                configuration["CncQuotation:ApprovedCommercialRulesVersion"],
                receipts is not null,
                receipts?.IsSharedDistributedAtomic == true)
            || claims is null)
        {
            return Results.NotFound();
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (Exception exception) when (exception is BadHttpRequestException or InvalidDataException)
        {
            return Retry("The CNC request could not be read safely. Refresh the page and try again.");
        }

        if (!CncSubmissionFormBinder.TryBind(form, out string formToken, out CncSubmission submission))
        {
            return Retry("The CNC quotation form contains ambiguous or invalid fields. Refresh the page and try again.");
        }

        var errors = new ModelStateDictionary();
        if (!CncSubmissionAdmission.IsCncRequestWithinBudgets(submission))
        {
            errors.AddModelError(nameof(CncSubmission.OrderItems), "The CNC request is too large to process safely. Please reduce the number or size of the items and try again.");
        }

        string sessionId = sessions.GetOrCreate(context);
        if (!bindings.TryValidateForm(formToken, sessionId, out CncProtectedForm? quotationForm) || quotationForm is null)
        {
            errors.AddModelError("QuotationFormToken", "This CNC quotation form has expired. Refresh the page and upload the files again.");
        }

        if (!errors.IsValid)
        {
            return Retry(errors);
        }

        CncSubmissionAdmission.ValidateSubmittedOrderItems(submission, sessionId, quotationForm!.FormId, bindings, errors);
        if (!errors.IsValid || !CncAuthenticatedProfile.TryValidateContact(submission, errors))
        {
            return Retry(errors);
        }

        if (!claims.TryClaim(submission, out CncReceiptClaimLease? lease) || lease is null)
        {
            return Retry("One or more CNC uploads have expired or were already submitted. Upload the affected files again.");
        }

        CncAuthenticatedProfileLoadResult account;
        try
        {
            account = await profiles.LoadAsync(context, errors, context.RequestAborted);
        }
        catch (Exception exception)
        {
            lease.TryRestoreBeforePersistence();
            logger.LogError("Authenticated CNC preparation failed before persistence. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Retry("We could not prepare your account for submission. Please try again.");
        }

        CncProfilePersistenceRequest? profileRequest = null;
        if (account.Outcome is not CncAuthenticatedProfileLoadOutcome.Anonymous)
        {
            if (account.Outcome is not CncAuthenticatedProfileLoadOutcome.Loaded
                || account.Profile is null
                || account.Customer is null
                || !account.Profile.TryMergeAndValidate(submission, errors, out CncSubmission? merged)
                || merged is null)
            {
                lease.TryRestoreBeforePersistence();
                return Retry(errors);
            }

            submission = merged;
            profileRequest = new CncProfilePersistenceRequest(
                0,
                account.Profile.CustomerId,
                account.Customer,
                Completion(submission));
        }

        if (!CncSubmissionAdmission.TryBuildReviewRecord(submission, out string review))
        {
            lease.TryRestoreBeforePersistence();
            return Retry("The generated CNC review record is too large to persist safely.");
        }

        if (!Guid.TryParse(sessionId, out Guid journeyId))
        {
            lease.TryRestoreBeforePersistence();
            return Retry("This CNC quotation session is invalid. Refresh the page and try again.");
        }

        var request = new CncRequestSubmission(
            new QuotationRequestSubmission(
                submission.FirstName,
                submission.LastName,
                submission.Email,
                NullIfBlank(submission.Telephone),
                submission.Country,
                NullIfBlank(submission.Company),
                NullIfBlank(submission.TaxNumber),
                review),
            journeyId);
        CncSubmissionPersistenceResult result;
        try
        {
            result = await persistence.ExecuteAsync(
                submission,
                lease,
                request,
                profileRequest,
                sessionId,
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                context.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError("CNC submission outcome became unconfirmed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Terminal(context, null, true, "We could not confirm whether your request was received. Do not submit it again; please contact info@maliev.com so we can check it safely.", journeyId);
        }

        if (result.Outcome == CncSubmissionPersistenceOutcome.Retry)
        {
            return Retry("Error preparing quotation request. Please try to contact us directly at info@maliev.com");
        }

        bool complete = result.Outcome == CncSubmissionPersistenceOutcome.Completed;
        string notification = result.RequestId is > 0
            ? complete
                ? "Thank you. Your CNC preliminary estimate and files were received for engineering review. We will send the binding quotation after review."
                : $"Request #{result.RequestId} was received, but a downstream step failed. Do not submit it again; please contact info@maliev.com with this reference."
            : "We could not confirm whether your request was received. Do not submit it again; please contact info@maliev.com so we can check it safely.";
        return Terminal(context, result.RequestId, !complete, notification, journeyId);
    }

    private IResult Terminal(HttpContext context, int? requestId, bool failed, string notification, Guid journeyId)
    {
        ITempDataDictionary tempData = tempDataFactory.GetTempData(context);
        tempData["Notification"] = notification;
        tempData["SubmissionFailed"] = failed;
        if (requestId is > 0)
        {
            tempData["SubmittedRequestId"] = requestId.Value;
            _ = LeadAnalyticsEventQueue.TryQueueInstantQuotation(
                tempData,
                requestId.Value,
                hasFiles: true,
                journeyId.ToString(),
                out _);
        }

        tempData.Save();
        return Results.Json(new { outcome = "terminal", redirectUrl = "/InstantQuotation/CNC-Machining" });
    }

    private static CncProfileCompletion Completion(CncSubmission value) => new(
        value.FirstName,
        value.LastName,
        value.Email,
        value.Mobile,
        NullIfBlank(value.Telephone),
        value.Company,
        value.FormattedTaxNumber,
        new(NullIfBlank(value.BillingBuilding), value.BillingStreet1, NullIfBlank(value.BillingStreet2),
            NullIfBlank(value.BillingCity), NullIfBlank(value.BillingProvince), NullIfBlank(value.BillingPostalCode)),
        value.Country,
        value.ShipToBillingAddress,
        value.ShipToBillingAddress
            ? null
            : new(NullIfBlank(value.ShippingBuilding), value.ShippingStreet1, NullIfBlank(value.ShippingStreet2),
                NullIfBlank(value.ShippingCity), NullIfBlank(value.ShippingProvince), NullIfBlank(value.ShippingPostalCode)),
        NullIfBlank(value.ShippingCountry));

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Retry(ModelStateDictionary errors) => Results.Json(new
    {
        outcome = "retry",
        errors = errors.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage).ToArray(),
    });

    private static IResult Retry(string error) => Results.Json(new { outcome = "retry", errors = new[] { error } });
}
