using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class InstantQuotationFulfillmentClient(
    ICountryClient countryClient,
    ICustomerProfileClient customerClient,
    ICustomerAuthenticationClient authenticationClient,
    ICustomerOrderCatalogClient catalogClient,
    ICustomerOrderSubmissionTransport orderTransport,
    INotificationClient notificationClient,
    ILogger<InstantQuotationFulfillmentClient> logger) : IInstantQuotationFulfillmentClient
{
    private const string CanonicalOrigin = "https://www.maliev.com";

    public async Task<InstantQuotationCustomerProvisionResult> ProvisionCustomerAsync(
        string? ownerIdentity,
        InstantQuotationCustomerSubmission customer,
        CancellationToken cancellationToken)
    {
        if (TryParseCustomerOwner(ownerIdentity, out var customerId))
        {
            return new(customerId, false, true, true);
        }

        var countries = await countryClient.GetCountriesAsync(cancellationToken);
        if (!countries.ServiceAvailable)
        {
            LogStageFailure("customer-country", "dependency_unavailable");
            return new(null, false, false, true);
        }

        var billingCountryId = ResolveCountryId(countries.Value ?? [], customer.Country);
        var shippingCountryId = customer.ShipToBillingAddress
            ? billingCountryId
            : ResolveCountryId(countries.Value ?? [], customer.ShippingCountry ?? customer.Country);
        if (billingCountryId is not > 0 || shippingCountryId is not > 0)
        {
            LogStageFailure("customer-country", "unexpected");
            return new(null, false, true, true);
        }

        return await customerClient.ProvisionInstantQuotationAsync(
            customer,
            billingCountryId.Value,
            shippingCountryId.Value,
            cancellationToken);
    }

    public async Task<InstantQuotationIdentityProvisionResult> ProvisionIdentityAsync(
        int customerId,
        InstantQuotationCustomerSubmission customer,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var registered = await authenticationClient.RegisterAsync(
            customerId,
            customer.Email.Trim(),
            temporaryPassword,
            cancellationToken);
        if (!registered.Succeeded
            && registered.Authorized
            && (registered.Conflict || !registered.ServiceAvailable))
        {
            registered = await authenticationClient.ResolveRegistrationAsync(
                customerId,
                customer.Email.Trim(),
                temporaryPassword,
                cancellationToken);
        }

        if (!registered.ServiceAvailable || !registered.Authorized)
        {
            LogStageFailure(
                "identity",
                registered.Authorized ? "dependency_unavailable" : "authorization");
        }

        return new(
            registered.Succeeded && registered.DatabaseId == customerId && registered.Created,
            registered.ServiceAvailable,
            registered.Authorized);
    }

    public async Task<InstantQuotationOrderProvisionResult> ProvisionOrderAsync(
        string submissionId,
        int partIndex,
        int customerId,
        string? customerDescription,
        InstantQuotationPart part,
        InstantQuotationPartQuote quote,
        int leadTimeDays,
        InstantQuotationFinalizedFile file,
        CancellationToken cancellationToken)
    {
        var materialInfo = PricingCatalog.ResolveMaterial(part.Configuration.MaterialKey);
        if (materialInfo is null || part.Configuration.Quantity <= 0)
        {
            LogStageFailure("order-input", "unexpected");
            return UnexpectedOrderFailure();
        }

        var catalogResult = await catalogClient.GetAsync(CustomerOrderKind.Additive, cancellationToken);
        if (!catalogResult.ServiceAvailable || catalogResult.Catalog is null)
        {
            LogStageFailure("order-catalog", "dependency_unavailable");
            return new(null, false, false, catalogResult.Authorized, false);
        }

        if (!catalogResult.Authorized)
        {
            LogStageFailure("order-catalog", "authorization");
            return new(null, false, true, false, false);
        }

        var processTerms = materialInfo.Process == PrintProcess.Resin
            ? new[] { "resin", "sla", "dlp", "lcd" }
            : new[] { "fdm", "fff", "filament", "fused" };
        var process = catalogResult.Catalog.Processes.FirstOrDefault(candidate =>
            processTerms.Any(term => candidate.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        if (process is null && catalogResult.Catalog.Processes.Count == 1)
        {
            process = catalogResult.Catalog.Processes[0];
        }

        var databaseMaterialName = DatabaseMaterialName(part.Configuration.MaterialKey);
        var material = catalogResult.Catalog.Materials.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, part.Configuration.MaterialKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Name, databaseMaterialName, StringComparison.OrdinalIgnoreCase)
            || candidate.Name.StartsWith(part.Configuration.MaterialKey + " ", StringComparison.OrdinalIgnoreCase)
            || candidate.Name.StartsWith(part.Configuration.MaterialKey + " —", StringComparison.OrdinalIgnoreCase));
        if (process is null || material is null)
        {
            LogStageFailure("order-catalog-mapping", "unexpected");
            return UnexpectedOrderFailure();
        }

        var options = await catalogClient.GetMaterialOptionsAsync(material.Id, cancellationToken);
        if (!options.ServiceAvailable || options.Options is null)
        {
            LogStageFailure("order-material-options", "dependency_unavailable");
            return new(null, false, false, options.Authorized, false);
        }

        if (!options.Authorized)
        {
            LogStageFailure("order-material-options", "authorization");
            return new(null, false, true, false, false);
        }

        var colorName = DatabaseColorName(part.Configuration.Color);
        var color = options.Options.Colors.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, colorName, StringComparison.OrdinalIgnoreCase));
        var finish = options.Options.SurfaceFinishes.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "As printed", StringComparison.OrdinalIgnoreCase));
        var currency = await catalogClient.GetCurrencyAsync("THB", cancellationToken);
        if (!currency.ServiceAvailable || currency.Value is not { Id: > 0 } thb || color is null || finish is null)
        {
            LogStageFailure("order-catalog-mapping", "unexpected");
            return UnexpectedOrderFailure();
        }

        var draft = new CustomerOrderDraft(
            CustomerOrderKind.Additive,
            OrderName(part.DisplayFileName),
            $"3D printing: {materialInfo.DisplayName}; {BuildPreferenceDescription(part.Configuration.BuildPreference)}",
            process.Id,
            material.Id,
            finish.Id,
            color.Id,
            part.Configuration.Quantity,
            AllowSocialMedia: false,
            Files: [],
            UnitPrice: Convert.ToDecimal(quote.UnitPrice, CultureInfo.InvariantCulture),
            CurrencyId: thb.Id,
            LeadTime: leadTimeDays,
            Comment: BuildOrderComment(submissionId, partIndex, part, quote, leadTimeDays, customerDescription),
            AllowCancellation: false);
        var orderKey = OperationKey(submissionId, partIndex, "order");
        var created = await orderTransport.CreateAsync(customerId, draft, orderKey, cancellationToken);
        if (created.OrderId is not > 0)
        {
            LogStageFailure("order-create", FailureCategory(created.ServiceAvailable, created.Authorized, created.Conflict));
            return new(null, false, created.ServiceAvailable, created.Authorized, created.Conflict);
        }

        var status = await orderTransport.AddNewStatusAsync(
            created.OrderId.Value,
            OperationKey(submissionId, partIndex, "status"),
            cancellationToken);
        if (!status.Succeeded)
        {
            LogStageFailure("order-status", FailureCategory(status.ServiceAvailable, status.Authorized, status.Conflict));
            return new(created.OrderId, false, status.ServiceAvailable, status.Authorized, status.Conflict);
        }

        var linked = await orderTransport.LinkAsync(
            customerId,
            created.OrderId.Value,
            new CustomerOrderUploadedObject(file.Bucket, file.ObjectName),
            cancellationToken);
        var result = new InstantQuotationOrderProvisionResult(
            created.OrderId,
            linked.Succeeded,
            linked.ServiceAvailable,
            linked.Authorized,
            linked.Conflict);
        if (!result.Succeeded)
        {
            LogStageFailure("order-file", FailureCategory(result.ServiceAvailable, result.Authorized, result.Conflict));
        }

        return result;
    }

    public async Task<bool> CompensateOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var compensated = await orderTransport.DeleteAsync(orderId, cancellationToken);
        if (!compensated) LogStageFailure("compensate-order", "dependency_unavailable");
        return compensated;
    }

    public async Task<bool> CompensateCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        var compensated = await customerClient.DeleteAsync(customerId, cancellationToken);
        if (!compensated) LogStageFailure("compensate-customer", "dependency_unavailable");
        return compensated;
    }

    public async Task<InstantQuotationWelcomePreparationResult> PrepareWelcomeAsync(
        InstantQuotationCustomerSubmission customer,
        CancellationToken cancellationToken)
    {
        var challenge = await authenticationClient.RequestEmailConfirmationAsync(
            customer.Email.Trim(),
            cancellationToken);
        return new(challenge.Token, challenge.ServiceAvailable, challenge.Authorized);
    }

    public async Task<NotificationResult> SendWelcomeAsync(
        InstantQuotationCustomerSubmission customer,
        string temporaryPassword,
        string confirmationToken,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var confirmationUrl = $"{CanonicalOrigin}/Account/EmailConfirmation"
            + $"?email={Uri.EscapeDataString(customer.Email.Trim())}"
            + $"&token={Uri.EscapeDataString(confirmationToken)}";
        var body = BuildWelcomeBody(
            customer.FirstName,
            customer.Email,
            temporaryPassword,
            confirmationUrl);
        return await notificationClient.SendIdempotentAsync(
            NotificationChannel.NoReply,
            new EmailNotification(
                customer.Email.Trim(),
                "Your MALIEV customer account / บัญชีลูกค้า MALIEV",
                body,
                null,
                null,
                ["mail-tracking@maliev.com"]),
            operationId,
            cancellationToken);
    }

    internal static string BuildWelcomeBody(
        string firstName,
        string email,
        string temporaryPassword,
        string confirmationUrl) =>
        "<!doctype html><html><body style=\"font-family:Arial,sans-serif;color:#172033;line-height:1.6\">"
        + $"<p>Hello {WebUtility.HtmlEncode(firstName)},</p>"
        + "<p>We created a MALIEV customer account for the order you just submitted.<br>เราได้สร้างบัญชีลูกค้า MALIEV สำหรับคำสั่งซื้อที่คุณเพิ่งส่ง</p>"
        + $"<p><strong>Email:</strong> {WebUtility.HtmlEncode(email)}<br><strong>Temporary password:</strong> <code>{WebUtility.HtmlEncode(temporaryPassword)}</code></p>"
        + $"<p><a href=\"{WebUtility.HtmlEncode(confirmationUrl)}\">Confirm your email / ยืนยันอีเมล</a></p>"
        + "<p>Please change this temporary password after your first sign-in.<br>กรุณาเปลี่ยนรหัสผ่านชั่วคราวนี้หลังจากเข้าสู่ระบบครั้งแรก</p>"
        + "</body></html>";

    private static bool TryParseCustomerOwner(string? ownerIdentity, out int customerId)
    {
        customerId = 0;
        const string prefix = "customer:";
        return ownerIdentity?.StartsWith(prefix, StringComparison.Ordinal) == true
            && int.TryParse(ownerIdentity[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out customerId)
            && customerId > 0;
    }

    private static int? ResolveCountryId(IReadOnlyList<Country> countries, string value)
    {
        var country = countries
            .Where(candidate => string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Iso2, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Iso3, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.CountryCode, value, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefault();
        return country?.Id;
    }

    internal static string DatabaseMaterialName(string key) => key.ToUpperInvariant() switch
    {
        "TPU" => "TPU (Shore 95A)",
        "PC" => "Polycarbonate (PC)",
        "M68" => "Resin Standard",
        "K" => "Resin Tough",
        "G217" => "Resin Clear",
        "F80" => "Elastic Resin",
        "CASTWAX" => "Castable Wax Resin",
        _ => key,
    };

    internal static string OrderName(string displayFileName)
    {
        var name = Path.GetFileName(displayFileName.Replace('\\', '/'));
        return name.Length <= 100 ? name : name[..100];
    }

    internal static string BuildOrderComment(
        string submissionId,
        int partIndex,
        InstantQuotationPart part,
        InstantQuotationPartQuote quote,
        int leadTimeDays,
        string? customerDescription)
    {
        var geometry = part.Geometry;
        var comment = new StringBuilder();
        comment.AppendLine(CultureInfo.InvariantCulture, $"Instant quotation {submissionId}/{partIndex + 1}");
        comment.AppendLine("Price status: customer estimate only; staff review required before payment.");
        comment.AppendLine(CultureInfo.InvariantCulture, $"Material key: {part.Configuration.MaterialKey}");
        comment.AppendLine(CultureInfo.InvariantCulture, $"Build: {BuildPreferenceDescription(part.Configuration.BuildPreference)}");
        comment.AppendLine(CultureInfo.InvariantCulture, $"Color: {part.Configuration.Color}");
        comment.AppendLine("Surface finish: As printed");
        comment.AppendLine(CultureInfo.InvariantCulture,
            $"Dimensions: {geometry.DimensionXmm:0.###} x {geometry.DimensionYmm:0.###} x {geometry.DimensionZmm:0.###} mm");
        comment.AppendLine(CultureInfo.InvariantCulture, $"Quantity: {part.Configuration.Quantity}");
        comment.AppendLine(CultureInfo.InvariantCulture, $"Submitted unit estimate: {quote.UnitPrice:0.00} THB");
        comment.AppendLine(CultureInfo.InvariantCulture, $"Submitted total estimate: {quote.Subtotal:0.00} THB");
        comment.AppendLine(CultureInfo.InvariantCulture, $"Unit print time: {quote.PrintTimeMinutesPerUnit:0.#} minutes");
        comment.AppendLine(CultureInfo.InvariantCulture,
            $"Total print time: {quote.PrintTimeMinutesPerUnit * part.Configuration.Quantity:0.#} minutes");
        comment.AppendLine(CultureInfo.InvariantCulture, $"Estimated lead time: up to {leadTimeDays} days");
        if (!geometry.IsManifold)
        {
            comment.AppendLine("Geometry warning: topology requires staff verification.");
        }

        if (!string.IsNullOrWhiteSpace(customerDescription))
        {
            comment.AppendLine(CultureInfo.InvariantCulture, $"Customer notes: {customerDescription.Trim()}");
        }

        return comment.ToString();
    }

    private static string DatabaseColorName(string value)
    {
        var color = value.Trim();
        if (string.Equals(color, "Any", StringComparison.OrdinalIgnoreCase)) return "Random color";
        if (color.StartsWith('#')) return "Other";
        if (string.Equals(color, "Natural", StringComparison.OrdinalIgnoreCase)) return "Raw";
        if (string.Equals(color, "Clear", StringComparison.OrdinalIgnoreCase)
            || string.Equals(color, "Translucent", StringComparison.OrdinalIgnoreCase)) return "Transparent";
        return color;
    }

    private static string BuildPreferenceDescription(BuildPreference preference) => preference switch
    {
        BuildPreference.Quality => "Quality",
        BuildPreference.Strength => "Strength",
        _ => "Standard",
    };

    private static string OperationKey(string submissionId, int partIndex, string purpose) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"instant-quotation-{purpose}:{submissionId.ToLowerInvariant()}:{partIndex}")));

    private static InstantQuotationOrderProvisionResult UnexpectedOrderFailure() =>
        new(null, false, true, true, false);

    private static string FailureCategory(bool available, bool authorized, bool conflict) =>
        !available ? "dependency_unavailable" : !authorized ? "authorization" : conflict ? "conflict" : "unexpected";

    private void LogStageFailure(string stage, string category) =>
        logger.LogWarning(
            "Instant quotation fulfillment stage {Stage} did not complete; category {Category}.",
            stage,
            category);
}
