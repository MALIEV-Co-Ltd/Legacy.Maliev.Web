using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Binds the established Razor Pages multipart field contract without accepting ambiguous values.</summary>
internal static class CncSubmissionFormBinder
{
    private const int MaximumItems = 20;
    private static readonly string[] SubmissionFields =
    [
        nameof(CncSubmission.FirstName), nameof(CncSubmission.LastName), nameof(CncSubmission.Company),
        nameof(CncSubmission.Email), nameof(CncSubmission.Mobile), nameof(CncSubmission.Telephone),
        nameof(CncSubmission.TaxNumber), nameof(CncSubmission.TaxBranch), nameof(CncSubmission.TaxBranchCode),
        nameof(CncSubmission.Description), nameof(CncSubmission.Country), nameof(CncSubmission.BillingBuilding),
        nameof(CncSubmission.BillingStreet1), nameof(CncSubmission.BillingStreet2), nameof(CncSubmission.BillingCity),
        nameof(CncSubmission.BillingProvince), nameof(CncSubmission.BillingPostalCode), nameof(CncSubmission.ShippingBuilding),
        nameof(CncSubmission.ShippingStreet1), nameof(CncSubmission.ShippingStreet2), nameof(CncSubmission.ShippingCity),
        nameof(CncSubmission.ShippingProvince), nameof(CncSubmission.ShippingPostalCode), nameof(CncSubmission.ShippingCountry),
    ];

    private static readonly string[] ItemFields =
    [
        nameof(ItemDetail.Process), nameof(ItemDetail.CncEstimateJson), nameof(ItemDetail.CncTolerance),
        nameof(ItemDetail.CncThreads), nameof(ItemDetail.CncRoughness), nameof(ItemDetail.CncInspection),
        nameof(ItemDetail.CncCertificate), nameof(ItemDetail.CncRequirementsNote), nameof(ItemDetail.CncDrawingFileName),
        nameof(ItemDetail.CncDrawingStoragePath), nameof(ItemDetail.CncItemId), nameof(ItemDetail.CncModelUploadReceipt),
        nameof(ItemDetail.CncDrawingUploadReceipt), nameof(ItemDetail.FileName), nameof(ItemDetail.StoragePath),
        nameof(ItemDetail.Dimension), nameof(ItemDetail.Quantity), nameof(ItemDetail.Material),
    ];

    internal static bool TryBind(IFormCollection form, out string formToken, out CncSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(form);
        submission = new CncSubmission();
        formToken = string.Empty;
        if (!TrySingle(form, "QuotationFormToken", out formToken))
        {
            return false;
        }

        foreach (string field in SubmissionFields)
        {
            if (!TryOptionalSingle(form, field, out string value))
            {
                return false;
            }

            typeof(CncSubmission).GetProperty(field)!.SetValue(submission, value);
        }

        if (!TryBoolean(form[nameof(CncSubmission.ShipToBillingAddress)], out bool shipToBilling))
        {
            return false;
        }

        submission.ShipToBillingAddress = shipToBilling;
        var itemKeys = form.Keys.Where(key => key.StartsWith("OrderItems[", StringComparison.Ordinal)).ToArray();
        if (itemKeys.Any(key => ParseItemIndex(key) is null))
        {
            return false;
        }

        var indexes = itemKeys
            .Select(ParseItemIndex)
            .Where(index => index.HasValue)
            .Select(index => index!.Value)
            .Distinct()
            .Order()
            .ToArray();
        if (indexes.Length > MaximumItems || indexes.Where((value, position) => value != position).Any())
        {
            return false;
        }

        foreach (int index in indexes)
        {
            var item = new ItemDetail();
            foreach (string field in ItemFields)
            {
                if (!TryOptionalSingle(form, $"OrderItems[{index.ToString(CultureInfo.InvariantCulture)}].{field}", out string value))
                {
                    return false;
                }

                typeof(ItemDetail).GetProperty(field)!.SetValue(item, value);
            }

            submission.OrderItems.Add(item);
        }

        return true;
    }

    private static int? ParseItemIndex(string key)
    {
        const string prefix = "OrderItems[";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int close = key.IndexOf(']', prefix.Length);
        return close > prefix.Length
            && close + 2 < key.Length
            && key[close + 1] == '.'
            && int.TryParse(key.AsSpan(prefix.Length, close - prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && index is >= 0 and < MaximumItems
                ? index
                : null;
    }

    private static bool TrySingle(IFormCollection form, string key, out string value)
    {
        StringValues values = form[key];
        value = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
        return values.Count == 1;
    }

    private static bool TryOptionalSingle(IFormCollection form, string key, out string value)
    {
        StringValues values = form[key];
        value = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
        return values.Count <= 1;
    }

    private static bool TryBoolean(StringValues values, out bool value)
    {
        value = false;
        if (values.Count == 0)
        {
            return true;
        }

        if (values.Count == 1)
        {
            return bool.TryParse(values[0], out value);
        }

        return values.Count == 2
            && bool.TryParse(values[0], out value)
            && value
            && bool.TryParse(values[1], out bool fallback)
            && !fallback;
    }
}
