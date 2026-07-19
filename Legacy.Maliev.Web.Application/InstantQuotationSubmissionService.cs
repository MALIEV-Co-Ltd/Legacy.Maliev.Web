using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.Web.Application;

public sealed class InstantQuotationSubmissionService(
    IInstantQuotationSessionStore sessionStore,
    IInstantQuotationPricingService pricingService,
    IQuotationClient quotationClient,
    IInstantQuotationSubmissionStore submissionStore,
    IInstantQuotationUploadClient uploadClient) : IInstantQuotationSubmissionService
{
    private const int SubmissionIdLength = 64;

    public async Task<InstantQuotationSubmissionResult> SubmitAsync(
        string sessionId,
        string? ownerIdentity,
        InstantQuotationCustomerSubmission customer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (string.IsNullOrWhiteSpace(sessionId)
            || (ownerIdentity is not null && string.IsNullOrWhiteSpace(ownerIdentity)))
        {
            return Rejected(InstantQuotationProblemCategory.Authorization);
        }

        InstantQuotationSessionState? session;
        try
        {
            session = await sessionStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Rejected(InstantQuotationProblemCategory.DependencyUnavailable);
        }
        catch (TimeoutException)
        {
            return Rejected(InstantQuotationProblemCategory.DependencyUnavailable);
        }

        if (session is null)
        {
            return Rejected(InstantQuotationProblemCategory.Authorization);
        }

        if (!IsValidCustomer(customer)
            || session.Parts is not { Count: > 0 }
            || !IsValidSubmissionId(session.SubmissionId))
        {
            return Rejected(InstantQuotationProblemCategory.Validation);
        }

        IInstantQuotationSubmissionLease? submissionLease;
        var checkpointOwnerIdentity = ownerIdentity ?? CreateAnonymousCheckpointOwnerIdentity(sessionId);
        try
        {
            submissionLease = await submissionStore.TryAcquireAsync(
                session.SubmissionId,
                checkpointOwnerIdentity,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Rejected(InstantQuotationProblemCategory.DependencyUnavailable);
        }
        catch (TimeoutException)
        {
            return Rejected(InstantQuotationProblemCategory.DependencyUnavailable);
        }

        if (submissionLease is null)
        {
            return Rejected(InstantQuotationProblemCategory.Conflict);
        }

        await using var acquiredSubmissionLease = submissionLease;

        InstantQuotationOrderQuote quote;
        try
        {
            quote = pricingService.Quote(session.RequestState);
        }
        catch (ArgumentException)
        {
            return Rejected(InstantQuotationProblemCategory.Validation);
        }

        if (quote.Parts.Count == 0 || quote.Parts.Count != session.Parts.Count)
        {
            return Rejected(InstantQuotationProblemCategory.Validation);
        }

        var snapshotDigest = CreateSnapshotDigest(session, quote);
        InstantQuotationSubmissionCheckpointRead checkpointRead;
        try
        {
            checkpointRead = await acquiredSubmissionLease.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Rejected(InstantQuotationProblemCategory.DependencyUnavailable);
        }
        catch (TimeoutException)
        {
            return Rejected(InstantQuotationProblemCategory.DependencyUnavailable);
        }

        if (!checkpointRead.LeaseValid)
        {
            return Rejected(InstantQuotationProblemCategory.Conflict);
        }

        var checkpoint = checkpointRead.Checkpoint;
        if (checkpoint is not null
            && !string.Equals(checkpoint.SubmissionId, session.SubmissionId, StringComparison.Ordinal))
        {
            return Rejected(InstantQuotationProblemCategory.Conflict);
        }

        if (checkpoint is not null && checkpoint.RequestReference <= 0)
        {
            return Rejected(InstantQuotationProblemCategory.Unexpected);
        }

        if (checkpoint is not null
            && !Enum.IsDefined(checkpoint.Status))
        {
            return Rejected(InstantQuotationProblemCategory.Unexpected);
        }

        if (checkpoint is not null
            && !string.Equals(checkpoint.SnapshotDigest, snapshotDigest, StringComparison.Ordinal))
        {
            return Rejected(InstantQuotationProblemCategory.Conflict);
        }

        if (checkpoint?.Status == InstantQuotationSubmissionCheckpointStatus.Completed)
        {
            return Completed(checkpoint.RequestReference);
        }

        if (checkpoint is null)
        {
            var creation = await CreateAndPersistRequestAsync(
                session,
                quote,
                snapshotDigest,
                acquiredSubmissionLease,
                customer,
                cancellationToken);
            if (creation.Result is not null)
            {
                return creation.Result;
            }

            checkpoint = creation.Checkpoint!;
        }

        return await FinalizeAsync(session, acquiredSubmissionLease, checkpoint, cancellationToken);
    }

    private async Task<(InstantQuotationSubmissionCheckpoint? Checkpoint, InstantQuotationSubmissionResult? Result)>
        CreateAndPersistRequestAsync(
            InstantQuotationSessionState session,
            InstantQuotationOrderQuote quote,
            string snapshotDigest,
            IInstantQuotationSubmissionLease submissionLease,
            InstantQuotationCustomerSubmission customer,
            CancellationToken cancellationToken)
    {
        var submission = new QuotationRequestSubmission(
            customer.FirstName.Trim(),
            customer.LastName.Trim(),
            customer.Email.Trim(),
            TrimToNull(customer.TelephoneNumber),
            customer.Country.Trim(),
            TrimToNull(customer.CompanyName),
            TrimToNull(customer.TaxIdentification),
            BuildMessage(session, quote, customer.Description));

        QuotationRequestResult quotationResult;
        try
        {
            quotationResult = await quotationClient.CreateRequestAsync(
                submission,
                $"legacy-web-instant-quotation-{session.SubmissionId.ToLowerInvariant()}",
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, Rejected(InstantQuotationProblemCategory.DependencyUnavailable));
        }
        catch (TimeoutException)
        {
            return (null, Rejected(InstantQuotationProblemCategory.DependencyUnavailable));
        }
        catch (HttpRequestException)
        {
            return (null, Rejected(InstantQuotationProblemCategory.DependencyUnavailable));
        }

        if (!quotationResult.ServiceAvailable)
        {
            return (null, Rejected(InstantQuotationProblemCategory.DependencyUnavailable));
        }

        if (!quotationResult.Authorized)
        {
            return (null, Rejected(InstantQuotationProblemCategory.Authorization));
        }

        if (quotationResult.ReferenceNumber is not > 0)
        {
            return (null, Rejected(InstantQuotationProblemCategory.Unexpected));
        }

        var checkpoint = new InstantQuotationSubmissionCheckpoint(
            session.SubmissionId,
            quotationResult.ReferenceNumber.Value,
            InstantQuotationSubmissionCheckpointStatus.Persisted,
            snapshotDigest);
        bool stored;
        try
        {
            stored = await submissionLease.TryPutAsync(
                checkpoint,
                expectedPriorStatus: null,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, Rejected(InstantQuotationProblemCategory.DependencyUnavailable));
        }
        catch (TimeoutException)
        {
            return (null, Rejected(InstantQuotationProblemCategory.DependencyUnavailable));
        }

        return stored
            ? (checkpoint, null)
            : (null, Rejected(InstantQuotationProblemCategory.Conflict));
    }

    private async Task<InstantQuotationSubmissionResult> FinalizeAsync(
        InstantQuotationSessionState session,
        IInstantQuotationSubmissionLease submissionLease,
        InstantQuotationSubmissionCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        InstantQuotationSubmissionCheckpointRead fencedRead;
        try
        {
            fencedRead = await submissionLease.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable);
        }
        catch (TimeoutException)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable);
        }

        if (!fencedRead.LeaseValid || fencedRead.Checkpoint != checkpoint)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.Conflict);
        }

        var expectedOperationId = CreateFinalizationOperationId(session.SubmissionId);
        InstantQuotationFinalizationResult finalization;
        try
        {
            finalization = await uploadClient.FinalizeAsync(
                session.SessionId,
                checkpoint.RequestReference,
                session.Parts.Select(part => part.UploadReference).ToArray(),
                expectedOperationId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable);
        }
        catch (TimeoutException)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable);
        }
        catch (HttpRequestException)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable);
        }

        if (finalization.ServiceStatus != InstantQuotationServiceStatus.Available)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable);
        }

        if (finalization.AuthorizationStatus != InstantQuotationAuthorizationStatus.Authorized)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.Authorization);
        }

        if (!string.Equals(finalization.OperationId, expectedOperationId, StringComparison.Ordinal))
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.Unexpected);
        }

        if (finalization.Status != InstantQuotationOperationStatus.Succeeded
            || finalization.ProblemCategory != InstantQuotationProblemCategory.None)
        {
            return Partial(
                checkpoint.RequestReference,
                finalization.ProblemCategory == InstantQuotationProblemCategory.None
                    ? InstantQuotationProblemCategory.Unexpected
                    : finalization.ProblemCategory);
        }

        var completedCheckpoint = checkpoint with
        {
            Status = InstantQuotationSubmissionCheckpointStatus.Completed,
        };
        bool stored;
        try
        {
            stored = await submissionLease.TryPutAsync(
                completedCheckpoint,
                InstantQuotationSubmissionCheckpointStatus.Persisted,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable);
        }
        catch (TimeoutException)
        {
            return Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable);
        }

        return stored
            ? Completed(checkpoint.RequestReference)
            : Partial(checkpoint.RequestReference, InstantQuotationProblemCategory.Conflict);
    }

    private static string BuildMessage(
        InstantQuotationSessionState session,
        InstantQuotationOrderQuote quote,
        string? description)
    {
        var message = new StringBuilder();
        message.AppendLine("Orders");
        for (var index = 0; index < quote.Parts.Count; index++)
        {
            var part = session.Parts[index];
            var partQuote = quote.Parts[index];
            message.AppendLine($"{index + 1} - {SingleLine(part.DisplayFileName)}");
            message.AppendLine($"Material: {partQuote.MaterialKey}");
            message.AppendLine($"Color: {SingleLine(partQuote.Color)}");
            message.AppendLine($"Height: {part.Geometry.HeightMm.ToString("0.###", CultureInfo.InvariantCulture)} mm");
            message.AppendLine($"Volume: {part.Geometry.VolumeMm3.ToString("0.###", CultureInfo.InvariantCulture)} mm3");
            message.AppendLine($"Quantity: {partQuote.Quantity.ToString(CultureInfo.InvariantCulture)} piece(s)");
            message.AppendLine($"Cost per unit: {partQuote.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture)} THB");
            message.AppendLine($"Print time per unit: {partQuote.PrintTimeMinutesPerUnit.ToString("0.##", CultureInfo.InvariantCulture)} minute(s)");
            message.AppendLine($"Total cost: {partQuote.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)} THB");
            message.AppendLine();
        }

        var cleanDescription = TrimToNull(description);
        if (cleanDescription is not null)
        {
            message.AppendLine("Description");
            using var reader = new StringReader(cleanDescription);
            while (reader.ReadLine() is { } line)
            {
                message.AppendLine(line);
            }

            message.AppendLine();
        }

        message.AppendLine(
            $"Due date: {quote.LeadTimeMinimumDays.ToString(CultureInfo.InvariantCulture)}-{quote.LeadTimeMaximumDays.ToString(CultureInfo.InvariantCulture)} business day(s)");
        message.AppendLine($"Total price: {quote.FinalOrderPrice.ToString("0.00", CultureInfo.InvariantCulture)} THB");
        return message.ToString();
    }

    private static bool IsValidCustomer(InstantQuotationCustomerSubmission customer)
    {
        if (string.IsNullOrWhiteSpace(customer.FirstName)
            || string.IsNullOrWhiteSpace(customer.LastName)
            || string.IsNullOrWhiteSpace(customer.Email)
            || string.IsNullOrWhiteSpace(customer.TelephoneNumber)
            || string.IsNullOrWhiteSpace(customer.Country)
            || ExceedsLength(customer.FirstName, 50)
            || ExceedsLength(customer.LastName, 50)
            || ExceedsLength(customer.Email, 50)
            || ExceedsLength(customer.TelephoneNumber, 50)
            || ExceedsLength(customer.Country, 50)
            || ExceedsLength(customer.CompanyName, 50)
            || ExceedsLength(customer.TaxIdentification, 50)
            || customer.Description?.Length > 512)
        {
            return false;
        }

        try
        {
            var address = new MailAddress(customer.Email.Trim());
            return string.Equals(address.Address, customer.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidSubmissionId(string submissionId) =>
        submissionId.Length == SubmissionIdLength
        && submissionId.All(Uri.IsHexDigit);

    private static string CreateSnapshotDigest(
        InstantQuotationSessionState session,
        InstantQuotationOrderQuote quote)
    {
        var snapshot = new StringBuilder();
        AppendSnapshotValue(snapshot, session.SessionId);
        AppendSnapshotValue(snapshot, session.SubmissionId);
        AppendSnapshotValue(snapshot, session.Parts.Count);
        for (var index = 0; index < session.Parts.Count; index++)
        {
            var part = session.Parts[index];
            var partQuote = quote.Parts[index];
            AppendSnapshotValue(snapshot, part.PartId.ToString("N"));
            AppendSnapshotValue(snapshot, part.DisplayFileName);
            AppendSnapshotValue(snapshot, part.UploadReference.Value);
            AppendSnapshotValue(snapshot, part.Geometry.HeightMm);
            AppendSnapshotValue(snapshot, part.Geometry.VolumeMm3);
            AppendSnapshotValue(snapshot, part.Geometry.FootprintMm2);
            AppendSnapshotValue(snapshot, part.Geometry.AreaProfileMm2.Count);
            foreach (var value in part.Geometry.AreaProfileMm2)
            {
                AppendSnapshotValue(snapshot, value);
            }

            AppendSnapshotValue(snapshot, part.Geometry.PerimeterProfileMm.Count);
            foreach (var value in part.Geometry.PerimeterProfileMm)
            {
                AppendSnapshotValue(snapshot, value);
            }

            AppendSnapshotValue(snapshot, part.Geometry.FacetCount);
            AppendSnapshotValue(snapshot, part.Geometry.BodyCount);
            AppendSnapshotValue(snapshot, part.Geometry.IsManifold);
            AppendSnapshotValue(snapshot, part.Configuration.MaterialKey);
            AppendSnapshotValue(snapshot, part.Configuration.Color);
            AppendSnapshotValue(snapshot, part.Configuration.Quantity);
            AppendSnapshotValue(snapshot, partQuote.PartId.ToString("N"));
            AppendSnapshotValue(snapshot, partQuote.MaterialKey);
            AppendSnapshotValue(snapshot, partQuote.Color);
            AppendSnapshotValue(snapshot, partQuote.Quantity);
            AppendSnapshotValue(snapshot, (int)partQuote.Process);
            AppendSnapshotValue(snapshot, partQuote.PrintTimeMinutesPerUnit);
            AppendSnapshotValue(snapshot, partQuote.MaterialPerUnit);
            AppendSnapshotValue(snapshot, partQuote.WeightGramsPerUnit);
            AppendSnapshotValue(snapshot, partQuote.BoundingCm3PerUnit);
            AppendSnapshotValue(snapshot, partQuote.UnitPrice);
            AppendSnapshotValue(snapshot, partQuote.Subtotal);
            AppendSnapshotValue(snapshot, partQuote.Tiers.Count);
            foreach (var tier in partQuote.Tiers)
            {
                AppendSnapshotValue(snapshot, tier.MinQuantity);
                AppendSnapshotValue(snapshot, tier.UnitPrice);
                AppendSnapshotValue(snapshot, tier.Active);
            }
        }

        AppendSnapshotValue(snapshot, quote.ItemsSubtotal);
        AppendSnapshotValue(snapshot, quote.Printing);
        AppendSnapshotValue(snapshot, quote.ShippingCost);
        AppendSnapshotValue(snapshot, quote.PriceBeforeVat);
        AppendSnapshotValue(snapshot, quote.Vat);
        AppendSnapshotValue(snapshot, quote.FinalOrderPrice);
        AppendSnapshotValue(snapshot, quote.LeadTimeMinimumDays);
        AppendSnapshotValue(snapshot, quote.LeadTimeMaximumDays);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private static void AppendSnapshotValue(StringBuilder snapshot, string value)
    {
        snapshot.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        snapshot.Append(':');
        snapshot.Append(value);
        snapshot.Append('|');
    }

    private static void AppendSnapshotValue(StringBuilder snapshot, int value) =>
        AppendSnapshotValue(snapshot, value.ToString(CultureInfo.InvariantCulture));

    private static void AppendSnapshotValue(StringBuilder snapshot, double value) =>
        AppendSnapshotValue(snapshot, value.ToString("R", CultureInfo.InvariantCulture));

    private static void AppendSnapshotValue(StringBuilder snapshot, bool value) =>
        AppendSnapshotValue(snapshot, value ? "1" : "0");

    private static string CreateFinalizationOperationId(string submissionId)
    {
        var value = Encoding.UTF8.GetBytes($"instant-quotation-finalize:{submissionId.ToLowerInvariant()}");
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static string CreateAnonymousCheckpointOwnerIdentity(string sessionId)
    {
        var value = Encoding.UTF8.GetBytes($"instant-quotation-anonymous-checkpoint-owner:{sessionId}");
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ExceedsLength(string? value, int maximumLength) =>
        value?.Length > maximumLength;

    private static InstantQuotationSubmissionResult Rejected(InstantQuotationProblemCategory problem) =>
        new(InstantQuotationSubmissionOutcome.Rejected, null, problem);

    private static InstantQuotationSubmissionResult Partial(
        int requestReference,
        InstantQuotationProblemCategory problem) =>
        new(InstantQuotationSubmissionOutcome.Partial, requestReference, problem);

    private static InstantQuotationSubmissionResult Completed(int requestReference) =>
        new(InstantQuotationSubmissionOutcome.Completed, requestReference, InstantQuotationProblemCategory.None);
}
