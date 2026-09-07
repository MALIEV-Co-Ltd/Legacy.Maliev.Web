using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal static partial class CncSubmissionAdmission
{
    internal static void ValidateSubmittedOrderItems(CncSubmission submission, string sessionId, string formId, CncProtectedUploadBindings bindings, ModelStateDictionary errors)
    {
        if (submission.OrderItems == null || submission.OrderItems.Count == 0)
        {
            errors.AddModelError(nameof(submission.OrderItems), "Upload at least one model file before submitting your quotation.");
            return;
        }

        var claimedCncReceiptNonces = new HashSet<string>(StringComparer.Ordinal);
        foreach (ItemDetail item in submission.OrderItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FileName))
            {
                errors.AddModelError(nameof(submission.OrderItems), "Every quotation item must include a completed file upload.");
                continue;
            }



            if (string.IsNullOrWhiteSpace(item.Material)
                || !int.TryParse(item.Quantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity)
                || quantity <= 0)
            {
                errors.AddModelError(nameof(submission.OrderItems), "Every quotation item must include a material and a positive whole-number quantity.");
            }

            {
                CncProtectedReceipt? modelReceipt = null;
                bool validModelReceipt = IsAllowedCncModelFile(item.FileName)
                    && bindings.TryValidateReceipt(
                        item.CncModelUploadReceipt,
                        item.CncItemId,
                        "model",
                        item.FileName,
                        item.StoragePath,
                        sessionId,
                        formId,
                        claimedCncReceiptNonces,
                        out modelReceipt);
                if (!validModelReceipt)
                {
                    errors.AddModelError(nameof(submission.OrderItems), "Every CNC model must have a valid server-issued upload receipt for this quotation item.");
                    continue;
                }

                item.ValidatedCncModelReceipt = modelReceipt!;
                item.StoragePath = modelReceipt!.StoragePath;

                bool validRequirements = string.Equals(item.Process, "CncMilling", StringComparison.Ordinal)
                    && IsAllowedCncRequirement(item.CncTolerance, "ISO 2768-m", "drawing")
                    && IsBoundedCncText(item.CncThreads, 500, required: true)
                    && IsAllowedCncRequirement(item.CncRoughness, "default", "3.2", "1.6", "tight")
                    && IsAllowedCncRequirement(item.CncInspection, "standard", "report", "enhanced")
                    && IsAllowedCncRequirement(item.CncCertificate, "required", "not-required")
                    && IsBoundedCncText(item.CncRequirementsNote, 2_000, required: false);
                bool drawingRequired = string.Equals(item.CncThreads, "drawing", StringComparison.Ordinal)
                    || !string.Equals(item.CncTolerance, "ISO 2768-m", StringComparison.Ordinal);
                bool hasAnyDrawingValue = !string.IsNullOrWhiteSpace(item.CncDrawingFileName)
                    || !string.IsNullOrWhiteSpace(item.CncDrawingStoragePath)
                    || !string.IsNullOrWhiteSpace(item.CncDrawingUploadReceipt);
                bool validDrawing = !drawingRequired && !hasAnyDrawingValue;
                if (hasAnyDrawingValue)
                {
                    CncProtectedReceipt? drawingReceipt = null;
                    validDrawing = IsAllowedCncDrawingFile(item.CncDrawingFileName)
                        && bindings.TryValidateReceipt(
                            item.CncDrawingUploadReceipt,
                            item.CncItemId,
                            "drawing",
                            item.CncDrawingFileName,
                            item.CncDrawingStoragePath,
                            sessionId,
                            formId,
                            claimedCncReceiptNonces,
                            out drawingReceipt);
                    if (validDrawing)
                    {
                        item.ValidatedCncDrawingReceipt = drawingReceipt!;
                        item.CncDrawingStoragePath = drawingReceipt!.StoragePath;
                    }
                }

                bool validSnapshot = ValidateCncSnapshotForPersistence(
                    item.CncEstimateJson,
                    out CncEstimateSnapshot snapshot,
                    out string canonicalJson);
                bool quantityMatches = validSnapshot
                    && int.TryParse(item.Quantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out int submittedQuantity)
                    && snapshot.QuantityPlan.Quantity == submittedQuantity;
                if (!validRequirements || !validDrawing || !validSnapshot || !quantityMatches)
                {
                    errors.AddModelError(
                        nameof(submission.OrderItems),
                        "Every CNC item must include a valid preliminary estimate, supported solid CAD model, bounded requirements, and its own owned PDF drawing when drawing-defined threads or tight tolerances are requested.");
                    continue;
                }

                item.ValidatedCncEstimate = snapshot;
                item.CanonicalCncEstimateJson = canonicalJson;
                continue;
            }
        }
    }

    internal static bool IsCncRequestWithinBudgets(CncSubmission submission)
    {
        if (submission.OrderItems == null || submission.OrderItems.Count == 0 || submission.OrderItems.Count > MaximumCncItemCount)
        {
            return false;
        }

        long snapshotBytes = 0;
        long generatedTextBytes = Encoding.UTF8.GetByteCount(string.Join('|',
            submission.FirstName, submission.LastName, submission.Company, submission.Email, submission.Mobile, submission.Telephone,
            submission.TaxNumber, submission.Description, string.Join('|', submission.BillingAddressLines), string.Join('|', submission.ShippingAddressLines)));
        foreach (ItemDetail item in submission.OrderItems)
        {
            if (item == null)
            {
                return false;
            }

            snapshotBytes += Encoding.UTF8.GetByteCount(item.CncEstimateJson ?? string.Empty);
            generatedTextBytes += Encoding.UTF8.GetByteCount(
                string.Join('|', item.FileName, item.Material, item.Quantity, item.Process, item.CncTolerance,
                    item.CncThreads, item.CncRoughness, item.CncInspection, item.CncCertificate, item.CncRequirementsNote,
                    item.CncItemId, item.StoragePath, item.CncModelUploadReceipt,
                    item.CncDrawingFileName, item.CncDrawingStoragePath, item.CncDrawingUploadReceipt));
            if (snapshotBytes > MaximumCncAggregateSnapshotUtf8Bytes
                || generatedTextBytes + (snapshotBytes * 2) > MaximumCncGeneratedTextUtf8Bytes)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedCncModelFile(string fileName) => !string.IsNullOrWhiteSpace(fileName) && new[] { ".step", ".stp", ".iges", ".igs" }.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    private static bool IsAllowedCncDrawingFile(string fileName) => !string.IsNullOrWhiteSpace(fileName) && string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);
}
