using System.Globalization;
using System.Text;
namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal static partial class CncSubmissionAdmission
{
    internal static bool TryBuildReviewRecord(CncSubmission submission, out string message)
    {
        StringBuilder requestBody = new StringBuilder();

        // Billing, shipping and the tax branch travel in the message body: the
        // quotation-request entity has no address columns, and adding them there would
        // be a schema change to a service this page only writes to. They move into real
        // customer and order records when the order is confirmed.
        requestBody.AppendLine("Contact");
        requestBody.AppendLine($"Mobile: {submission.Mobile}");
        if (!string.IsNullOrWhiteSpace(submission.Telephone))
        {
            requestBody.AppendLine($"Office phone: {submission.Telephone}");
        }

        if (!string.IsNullOrWhiteSpace(submission.TaxNumber))
        {
            requestBody.AppendLine($"Tax number: {submission.FormattedTaxNumber}");
        }

        requestBody.AppendLine();
        requestBody.AppendLine("Billing address");
        foreach (string line in submission.BillingAddressLines)
        {
            requestBody.AppendLine(line);
        }

        requestBody.AppendLine();
        requestBody.AppendLine(submission.ShipToBillingAddress ? "Shipping address (same as billing)" : "Shipping address");
        foreach (string line in submission.ShippingAddressLines)
        {
            requestBody.AppendLine(line);
        }

        requestBody.AppendLine();

        if (submission.OrderItems != null && submission.OrderItems.Count > 0)
        {
            requestBody.AppendLine($"Orders");

            foreach (var item in submission.OrderItems)
            {
                requestBody.AppendLine($"{submission.OrderItems.IndexOf(item) + 1} - {item.FileName}");
                requestBody.AppendLine(BuildCncReviewSummary(
                    item,
                    item.ValidatedCncEstimate,
                    item.CanonicalCncEstimateJson));
                requestBody.AppendLine();
            }
        }

        if (!string.IsNullOrEmpty(submission.Description))
        {
            requestBody.AppendLine($"Description");
            using (StringReader reader = new StringReader(submission.Description))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    requestBody.AppendLine($"{line}");
                }
            }

            requestBody.AppendLine();
        }

        {
            requestBody.AppendLine("Commercial status: preliminary client-generated estimates only; engineering review is required before a binding quotation, order, invoice or payment.");
        }


        message = requestBody.ToString();
        return Encoding.UTF8.GetByteCount(message) <= MaximumCncGeneratedTextUtf8Bytes;
    }
    internal static string BuildCncReviewSummary(ItemDetail item, CncEstimateSnapshot snapshot, string canonicalJson)
    {
        if (item == null || snapshot == null || string.IsNullOrWhiteSpace(canonicalJson))
        {
            return string.Empty;
        }

        string reasons = snapshot.ReviewReasons.Count == 0 ? "none" : string.Join(", ", snapshot.ReviewReasons);
        string geometryReasons = snapshot.GeometrySummary.ReviewReasons.Count == 0 ? "none" : string.Join(", ", snapshot.GeometrySummary.ReviewReasons);
        string stockReasons = snapshot.StockPlan.ReviewReasons.Count == 0 ? "none" : string.Join(", ", snapshot.StockPlan.ReviewReasons);
        var summary = new StringBuilder();
        summary.AppendLine("CNC preliminary estimate (client generated; engineering review required)");
        summary.AppendLine($"Process: {item.Process}");
        summary.AppendLine($"Calculated at UTC: {snapshot.CalculatedAtUtc}");
        summary.AppendLine($"Versions: estimator={snapshot.Versions.EstimatorVersion}; stock={snapshot.Versions.StockRulesVersion}; material-price={snapshot.Versions.MaterialPriceModelVersion}; commercial={snapshot.Versions.CommercialRulesVersion}");
        summary.AppendLine($"Confidence: {snapshot.Confidence}; review reasons: {reasons}");
        summary.AppendLine($"Geometry: {snapshot.GeometrySummary.Category}; confidence={snapshot.GeometrySummary.Confidence}; review reasons={geometryReasons}");
        summary.AppendLine($"Stock: {snapshot.StockPlan.Strategy}; pieces={snapshot.StockPlan.Pieces}; buffered piece before VAT={CncNumber(snapshot.StockPlan.BufferedPiecePriceBeforeVat)} THB; supplier shipping before VAT={CncNumber(snapshot.StockPlan.SupplierShippingBeforeVat)} THB; confidence={snapshot.StockPlan.Confidence}; review reasons={stockReasons}");
        summary.AppendLine($"Setups: {snapshot.SetupPlan.SetupCount} ({snapshot.SetupPlan.Category})");
        summary.AppendLine($"Operations: {snapshot.OperationSummary.OperationCount} ({snapshot.OperationSummary.Category}); estimated minutes per part={CncNumber(snapshot.EstimatedMinutesPerPart)}");
        summary.AppendLine($"Quantity: {snapshot.QuantityPlan.Quantity} ({snapshot.QuantityPlan.Category})");
        summary.AppendLine($"Customer requirements: material={item.Material}; tolerance={item.CncTolerance}; threads={item.CncThreads}; roughness={item.CncRoughness}; inspection={item.CncInspection}; certificate={item.CncCertificate}; note={item.CncRequirementsNote ?? string.Empty}");
        summary.AppendLine($"Technical drawing: {(string.IsNullOrWhiteSpace(item.CncDrawingFileName) ? "not supplied" : item.CncDrawingFileName)}");
        summary.AppendLine($"Before VAT: {CncNumber(snapshot.EstimatedPriceBeforeVat)} THB");
        summary.AppendLine($"VAT rate: {CncNumber(snapshot.VatRate)}");
        summary.AppendLine($"After VAT: {CncNumber(snapshot.EstimatedPriceAfterVat)} THB");
        summary.AppendLine($"Currency: {snapshot.Currency}");
        summary.AppendLine($"Canonical untrusted snapshot: {canonicalJson}");
        return summary.ToString().TrimEnd();
    }

    private static string CncNumber(double value) => value.ToString("0.################", CultureInfo.InvariantCulture);


}
