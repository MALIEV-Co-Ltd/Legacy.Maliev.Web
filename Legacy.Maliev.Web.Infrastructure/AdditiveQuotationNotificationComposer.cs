using System.Globalization;
using System.Net;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Infrastructure;

// Pure composition only. Inputs must come from the frozen server-authoritative submission.
internal sealed record AdditiveQuotationNotificationItem(
    string FileName, string Material, string Color, string Dimensions, string? GeometryWarning,
    int Quantity, decimal UnitPrice, decimal Subtotal, PrintProcess Process, BuildPreference BuildPreference,
    Uri DownloadUrl, decimal UnitPrintTimeMinutes, decimal TotalPrintTimeMinutes);

internal sealed record AdditiveQuotationNotificationSummary(
    int RequestReference, InstantQuotationCustomerSubmission Customer,
    IReadOnlyList<AdditiveQuotationNotificationItem> Items, decimal FinalOrderPrice, int LeadTimeMaximumDays,
    bool DomesticShippingPriced, string DestinationCountryCode, int LeadTimeMinimumDays);

internal sealed record AdditiveQuotationNotificationPlan(
    NotificationChannel Channel, EmailNotification Customer, EmailNotification Manufacturing);

internal static class AdditiveQuotationNotificationComposer
{
    internal static AdditiveQuotationNotificationPlan Compose(AdditiveQuotationNotificationSummary summary)
    {
        if (summary.RequestReference <= 0 || summary.Items.Count is < 1 or > 32
            || summary.FinalOrderPrice < 0 || summary.LeadTimeMaximumDays <= 0
            || summary.LeadTimeMinimumDays <= 0 || summary.LeadTimeMinimumDays > summary.LeadTimeMaximumDays
            || summary.Items.Any(item => item.Quantity <= 0 || item.UnitPrice < 0 || item.Subtotal < 0
                || item.UnitPrintTimeMinutes < 0 || item.TotalPrintTimeMinutes < 0
                || !item.DownloadUrl.IsAbsoluteUri || item.DownloadUrl.Scheme != Uri.UriSchemeHttps
                || item.DownloadUrl.UserInfo.Length != 0 || !Enum.IsDefined(item.Process)
                || !Enum.IsDefined(item.BuildPreference)))
        {
            throw new ArgumentException("Invalid authoritative quotation summary.", nameof(summary));
        }

        var customer = summary.Customer;
        var subject = "Quotation Request #" + summary.RequestReference.ToString(CultureInfo.InvariantCulture);
        return new(NotificationChannel.Manufacturing,
            new(customer.Email, subject, Body(summary, false), null, null, ["mail-tracking@maliev.com"]),
            new("manufacturing@maliev.com", subject, Body(summary, true), customer.Email, null, null));
    }

    private static string Body(AdditiveQuotationNotificationSummary summary, bool manufacturing)
    {
        var customer = summary.Customer;
        var body = new StringBuilder("<!doctype html><html><body>");
        Field(body, "Hello", manufacturing ? "Manufacturing Team" : customer.FirstName + " " + customer.LastName);
        Field(body, "Review", manufacturing
            ? "A customer sent files for manufacturability review. Please review the request and get back with a final quotation."
            : "Thank you for your order. We will review all the files and get back to you with the final quotation. If any file fails our printability tests, we will explain how to improve the part.");
        Field(body, "Status", "Estimated quotation only; staff review required before final quotation or payment.");
        Field(body, "First name", customer.FirstName);
        Field(body, "Last name", customer.LastName);
        Field(body, "Company", customer.CompanyName);
        Field(body, "Tax ID", customer.TaxIdentification);
        Contact(body, "Email", "mailto", customer.Email);
        Contact(body, "Mobile", "tel", customer.MobileNumber);
        Contact(body, "Office phone", "tel", customer.TelephoneNumber);
        Field(body, "Billing address", Address(customer, false));
        Field(body, customer.ShipToBillingAddress ? "Shipping address (same as billing)" : "Shipping address", Address(customer, !customer.ShipToBillingAddress));
        foreach (var item in summary.Items)
        {
            body.Append("<div><a href=\"").Append(Html(item.DownloadUrl.AbsoluteUri)).Append("\"><strong>")
                .Append(Html(item.FileName)).Append("</strong></a></div>");
            Field(body, "Material", item.Material);
            Field(body, "Build", Build(item, manufacturing));
            Field(body, "Color", item.Color);
            Field(body, "Size", item.Dimensions + " mm");
            if (!string.IsNullOrEmpty(item.GeometryWarning)) Field(body, "Geometry warning", item.GeometryWarning);
            Field(body, "Quantity", item.Quantity.ToString(CultureInfo.InvariantCulture) + " piece(s)");
            Field(body, "Price per unit", Money(item.UnitPrice));
            Field(body, "Subtotal", Money(item.Subtotal));
            if (manufacturing)
            {
                Field(body, "Print time per unit", item.UnitPrintTimeMinutes.ToString("0.#", CultureInfo.InvariantCulture) + " minutes");
                Field(body, "Total print time", item.TotalPrintTimeMinutes.ToString("0.#", CultureInfo.InvariantCulture) + " minutes");
            }
        }
        foreach (var line in (customer.Description ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            if (line.Length != 0) Field(body, "Note", line);
        Field(body, "Lead time", summary.LeadTimeMinimumDays == summary.LeadTimeMaximumDays
            ? "up to " + summary.LeadTimeMaximumDays.ToString(CultureInfo.InvariantCulture) + " days"
            : FormattableString.Invariant($"{summary.LeadTimeMinimumDays}-{summary.LeadTimeMaximumDays} days"));
        Field(body, "Estimated price", Money(summary.FinalOrderPrice));
        Field(body, "Estimate includes", summary.DomesticShippingPriced
            ? "Includes 7% VAT and estimated domestic shipping. International shipping is quoted separately."
            : "Includes 7% VAT. International shipping is excluded; quoted separately. VAT is calculated only on the included amount.");
        Field(body, "Shipping to " + Html(summary.DestinationCountryCode), summary.DomesticShippingPriced
            ? "Domestic Thailand rate included (Flash Express)."
            : "To be quoted (excluded from total).");
        if (!manufacturing)
        {
            Field(body, "Questions", "If you have questions or need changes to your order, please write us back to this email.");
            body.Append("<div>Best regards,</div><a href=\"https://www.maliev.com/\">MALIEV</a>");
            Field(body, "Address", "36/1 Moo 3, Khlong Khoi, Pak Kret, Nonthaburi 11120, Thailand");
            Field(body, "Tel.", "+66(0)81-803-0404 / +66(0)89-895-0690");
        }
        return body.Append("</body></html>").ToString();
    }

    private static string Build(AdditiveQuotationNotificationItem item, bool detail)
    {
        if (item.Process == PrintProcess.Resin)
            return detail ? "Standard resin - 0.05 mm layers, full-layer exposure, wash and post-cure" : "Standard resin";
        return (item.BuildPreference, detail) switch
        {
            (BuildPreference.Quality, true) => "Quality - 0.12 mm layers, reduced speed and acceleration, Gyroid sparse infill",
            (BuildPreference.Strength, true) => "Strength - 6 walls, 2 mm top and bottom shells, denser sparse infill",
            (BuildPreference.Standard, true) => FormattableString.Invariant($"Standard - {PricingCatalog.FdmLayerHeightMm:0.00} mm layers, {PricingCatalog.FdmWallCount} walls, {PricingCatalog.FdmInfillDensity:P0} sparse infill"),
            (BuildPreference.Quality, false) => "Quality",
            (BuildPreference.Strength, false) => "Strength",
            _ => "Standard",
        };
    }

    private static string Address(InstantQuotationCustomerSubmission customer, bool shipping) => string.Join(", ",
        (shipping
            ? new[] { customer.ShippingBuilding, customer.ShippingAddressLine1, customer.ShippingAddressLine2, customer.ShippingCity, customer.ShippingProvince, customer.ShippingPostalCode, customer.ShippingCountry ?? customer.Country }
            : new[] { customer.BillingBuilding, customer.BillingAddressLine1, customer.BillingAddressLine2, customer.BillingCity, customer.BillingProvince, customer.BillingPostalCode, customer.Country })
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture) + " THB";
    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? "-");
    private static void Field(StringBuilder body, string label, string? value) =>
        body.Append("<div>").Append(label).Append(": ").Append(Html(value)).Append("</div>");
    private static void Contact(StringBuilder body, string label, string scheme, string? value)
    {
        if (string.IsNullOrEmpty(value)) { Field(body, label, null); return; }
        body.Append("<div>").Append(label).Append(": <a href=\"").Append(scheme).Append(':')
            .Append(Html(Uri.EscapeDataString(value))).Append("\">").Append(Html(value)).Append("</a></div>");
    }
}
