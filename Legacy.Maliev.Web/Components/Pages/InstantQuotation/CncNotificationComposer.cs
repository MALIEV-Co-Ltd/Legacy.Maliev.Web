using System.Globalization;
using System.Text;
using Legacy.Maliev.Web.Application;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal sealed record CncNotificationItemLinks(Uri Model, Uri? Drawing = null);

internal sealed record CncNotificationPlan(
    NotificationChannel CustomerChannel,
    EmailNotification Customer,
    NotificationChannel ManufacturingChannel,
    EmailNotification Manufacturing);

/// <summary>Builds the two source-compatible CNC request notifications after persistence completes.</summary>
internal static class CncNotificationComposer
{
    private const int MaximumBodyLength = 100_000;
    private const string ManufacturingAddress = "manufacturing@maliev.com";
    private const string TrackingAddress = "mail-tracking@maliev.com";

    internal static bool TryCompose(
        CncSubmission submission,
        int requestId,
        IReadOnlyList<CncNotificationItemLinks> links,
        out CncNotificationPlan? plan)
    {
        plan = null;
        if (submission is null || requestId <= 0 || submission.OrderItems is null || submission.OrderItems.Count == 0
            || links is null || links.Count != submission.OrderItems.Count || string.IsNullOrWhiteSpace(submission.Email))
        {
            return false;
        }

        for (var index = 0; index < submission.OrderItems.Count; index++)
        {
            ItemDetail item = submission.OrderItems[index];
            CncNotificationItemLinks itemLinks = links[index];
            if (item is null || item.ValidatedCncEstimate is null || string.IsNullOrWhiteSpace(item.CanonicalCncEstimateJson)
                || !IsSafeLink(itemLinks.Model)
                || (string.IsNullOrWhiteSpace(item.CncDrawingStoragePath) != (itemLinks.Drawing is null))
                || (itemLinks.Drawing is not null && !IsSafeLink(itemLinks.Drawing)))
            {
                return false;
            }
        }

        string customerBody = CustomerBody(submission, links);
        string manufacturingBody = ManufacturingBody(submission, links);
        if (customerBody.Length > MaximumBodyLength || manufacturingBody.Length > MaximumBodyLength)
        {
            return false;
        }

        string subject = $"Quotation Request #{requestId}";
        plan = new CncNotificationPlan(
            NotificationChannel.Manufacturing,
            new EmailNotification(submission.Email, subject, customerBody, null, null, [TrackingAddress]),
            NotificationChannel.Manufacturing,
            new EmailNotification(ManufacturingAddress, subject, manufacturingBody, submission.Email, null, null));
        return true;
    }

    private static string CustomerBody(CncSubmission submission, IReadOnlyList<CncNotificationItemLinks> links)
    {
        var content = new StringBuilder();
        content.AppendLine($"<div>Hello {Html(submission.FirstName)} {Html(submission.LastName)},</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<div>Thank you for your CNC engineering-review request.</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<div>Your browser-generated price is preliminary and untrusted. Engineering may update it after reviewing geometry, stock, setups, operations and requirements; it is not an order confirmation or payment authorization.</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<div>We have received the following information for your order.</div>");
        AppendContact(content, submission);
        AppendAddresses(content, submission);
        AppendItems(content, submission, links, customer: true);
        AppendDescription(content, submission.Description);
        content.AppendLine("<div><strong>These client-generated preliminary estimates are assumptions for engineering review. Engineering may update the price and lead time before issuing the binding quotation.</strong></div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<div>If you have any questions or needed to make some changes to your order. Please feel free to write us back to this email.</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<div>Best regards,</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<a href='https://www.maliev.com/'><img class='max-width' style='display:block;color:#000000;text-decoration:none;font-family:Helvetica, arial, sans-serif;font-size:16px;' data-proportionally-constrained='true' src='https://storage.googleapis.com/maliev.com/web-contents/images/logo-black-20px.png' alt='Maliev Co., Ltd.' width='87' height='20' border='0'></a>");
        content.AppendLine("<div><span style='font-size:12px; font-family:Arial, Helvetica, sans-serif''>36/1 Moo 3</span></div>");
        content.AppendLine("<div><span style='font-size:12px; font-family:Arial, Helvetica, sans-serif''>Khlong Khoi, Pak Kret,</span></div>");
        content.AppendLine("<div><span style='font-size:12px; font-family:Arial, Helvetica, sans-serif''>Nonthaburi 11120, Thailand</span></div>");
        content.AppendLine("<div><span style='font-size:12px; font-family:Arial, Helvetica, sans-serif''>Tel.: +66(0)81-803-0404</span></div>");
        content.AppendLine("<div><span style='font-size:12px; font-family:Arial, Helvetica, sans-serif''>Tel.: +66(0)89-895-0690</span></div>");
        content.AppendLine("<div><span style='font-size:12px; font-family:Arial, Helvetica, sans-serif''>www.maliev.com</span></div>");
        return content.ToString();
    }

    private static string ManufacturingBody(CncSubmission submission, IReadOnlyList<CncNotificationItemLinks> links)
    {
        var content = new StringBuilder();
        content.AppendLine("<div>Hello Manufacturing Team,</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<div>A customer sent in their file(s) for manufacturability review.</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<div>The CNC prices below are client generated, untrusted and preliminary. Engineering review is required before any binding quotation, order, invoice or payment.</div>");
        content.AppendLine("<div>Please review the request and get back with a final quotation as soon as possible.</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine("<div><strong>Customer Information</strong></div>");
        content.AppendLine($"<div>First name: {Html(submission.FirstName)}</div>");
        content.AppendLine($"<div>Last name: {Html(submission.LastName)}</div>");
        AppendContact(content, submission);
        AppendAddresses(content, submission);
        AppendItems(content, submission, links, customer: false);
        AppendDescription(content, submission.Description);
        content.AppendLine("<div><strong>Engineering must issue or approve the binding commercial quotation after review.</strong></div>");
        return content.ToString();
    }

    private static void AppendContact(StringBuilder content, CncSubmission submission)
    {
        content.AppendLine($"<div>Company: {(string.IsNullOrEmpty(submission.Company) ? "-" : Html(submission.Company))}</div>");
        content.AppendLine($"<div>Tax ID: {(string.IsNullOrEmpty(submission.TaxNumber) ? "-" : Html(submission.FormattedTaxNumber))}</div>");
        content.AppendLine($"<div>Email: <a href='mailto:{Html(Uri.EscapeDataString(submission.Email ?? string.Empty))}'>{Html(submission.Email)}</a></div>");
        content.AppendLine($"<div>Mobile: <a href='tel:{Html(Uri.EscapeDataString(submission.Mobile ?? string.Empty))}'>{Html(submission.Mobile)}</a></div>");
        if (!string.IsNullOrEmpty(submission.Telephone))
        {
            content.AppendLine($"<div>Office phone: <a href='tel:{Html(Uri.EscapeDataString(submission.Telephone))}'>{Html(submission.Telephone)}</a></div>");
        }

        content.AppendLine("<div>&nbsp;</div>");
    }

    private static void AppendAddresses(StringBuilder content, CncSubmission submission)
    {
        content.AppendLine("<div><strong>Billing address</strong></div>");
        content.AppendLine($"<div>{Address(submission.BillingAddressLines)}</div>");
        content.AppendLine("<div>&nbsp;</div>");
        content.AppendLine($"<div><strong>Shipping address</strong>{(submission.ShipToBillingAddress ? " (same as billing)" : string.Empty)}</div>");
        content.AppendLine($"<div>{Address(submission.ShippingAddressLines)}</div>");
        content.AppendLine("<div>&nbsp;</div>");
    }

    private static void AppendItems(StringBuilder content, CncSubmission submission, IReadOnlyList<CncNotificationItemLinks> links, bool customer)
    {
        content.AppendLine("<div><strong>Order Items</strong></div>");
        content.AppendLine("<ol>");
        for (var index = 0; index < submission.OrderItems.Count; index++)
        {
            ItemDetail item = submission.OrderItems[index];
            CncNotificationItemLinks itemLinks = links[index];
            content.AppendLine("<li>");
            content.AppendLine($"<div><a href='{Html(itemLinks.Model.AbsoluteUri)}'><strong>{Html(item.FileName)}</strong></a></div>");
            if (itemLinks.Drawing is not null)
            {
                content.AppendLine($"<div>Technical drawing: <a href='{Html(itemLinks.Drawing.AbsoluteUri)}'><strong>{Html(item.CncDrawingFileName)}</strong></a></div>");
            }

            if (customer)
            {
                AppendCustomerEstimate(content, item);
            }
            else
            {
                foreach (string line in BuildCncReviewSummary(item, item.ValidatedCncEstimate, item.CanonicalCncEstimateJson).Split('\n'))
                {
                    content.AppendLine($"<div>{Html(line.TrimEnd('\r'))}</div>");
                }
            }

            content.AppendLine("<div>&nbsp;</div>");
            content.AppendLine("</li>");
        }

        content.AppendLine("</ol>");
    }

    private static void AppendCustomerEstimate(StringBuilder content, ItemDetail item)
    {
        CncEstimateSnapshot snapshot = item.ValidatedCncEstimate;
        content.AppendLine($"<div>Process: {Html(item.Process)}</div>");
        content.AppendLine($"<div>Material: {Html(item.Material)}</div>");
        content.AppendLine($"<div>Quantity: {snapshot.QuantityPlan.Quantity} piece(s)</div>");
        content.AppendLine($"<div>Preliminary before VAT: {CncNumber(snapshot.EstimatedPriceBeforeVat)} THB</div>");
        content.AppendLine($"<div>VAT rate: {CncNumber(snapshot.VatRate)}</div>");
        content.AppendLine($"<div><strong>Preliminary after VAT: {CncNumber(snapshot.EstimatedPriceAfterVat)} THB</strong></div>");
        content.AppendLine($"<div>Confidence: {Html(snapshot.Confidence)}</div>");
        content.AppendLine("<div>Engineering review: required before the official quotation.</div>");
        content.AppendLine($"<div>Requirements: tolerance={Html(item.CncTolerance)}; threads={Html(item.CncThreads)}; roughness={Html(item.CncRoughness)}; inspection={Html(item.CncInspection)}; certificate={Html(item.CncCertificate)}; note={Html(item.CncRequirementsNote ?? string.Empty)}</div>");
        content.AppendLine("<div><strong>Engineering may update this preliminary price after reviewing the part. This is not an order confirmation or payment authorization.</strong></div>");
    }

    private static void AppendDescription(StringBuilder content, string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return;
        }

        content.AppendLine("<div><strong>Description</strong></div>");
        using var reader = new StringReader(description);
        while (reader.ReadLine() is { } line)
        {
            content.AppendLine($"<div>{Html(line)}</div>");
        }

        content.AppendLine("<div>&nbsp;</div>");
    }

    private static bool IsSafeLink(Uri link) => link.IsAbsoluteUri
        && link.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(link.Host)
        && string.IsNullOrEmpty(link.UserInfo);

    private static string Address(IReadOnlyList<string> lines) => lines is null || lines.Count == 0
        ? "-"
        : string.Join("<br />", lines.Select(Html));

    private static string Html(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string CncNumber(double value) => value.ToString("0.################", CultureInfo.InvariantCulture);
}
