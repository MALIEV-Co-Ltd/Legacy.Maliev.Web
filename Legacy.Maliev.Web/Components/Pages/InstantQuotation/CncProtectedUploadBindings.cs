using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Protects CNC form and receipt identities independently of HTTP and storage.</summary>
internal sealed class CncProtectedUploadBindings(IDataProtectionProvider provider, TimeProvider clock)
{
    internal const string FormPurpose = "Maliev.Web.CncQuotationForm.v1";
    internal const string ReceiptPurpose = "Maliev.Web.CncUploadReceipt.v1";
    internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(3);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector formProtector = provider.CreateProtector(FormPurpose);
    private readonly IDataProtector receiptProtector = provider.CreateProtector(ReceiptPurpose);

    internal string CreateFormToken(string sessionId)
    {
        var now = clock.GetUtcNow();
        return formProtector.Protect(JsonSerializer.Serialize(new CncProtectedForm(Guid.NewGuid().ToString("N"), sessionId, now, now.Add(Lifetime)), Json));
    }

    internal bool TryValidateForm(string? token, string sessionId, out CncProtectedForm? form)
    {
        form = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return false;
        if (!TryRead(formProtector, token, out form)) return false;
        return form is not null && form.FormId?.Length == 32 && Guid.TryParseExact(form.FormId, "N", out _)
            && string.Equals(form.SessionId, sessionId, StringComparison.Ordinal)
            && ValidTime(form.IssuedAtUtc, form.ExpiresAtUtc);
    }

    internal string CreateReceiptToken(string sessionId, string formId, string itemId, string role, string originalFileName, string storagePath)
    {
        var now = clock.GetUtcNow();
        var receipt = new CncProtectedReceipt(sessionId, formId, itemId, role, originalFileName, storagePath, now, now.Add(Lifetime), Guid.NewGuid().ToString("N"));
        return receiptProtector.Protect(JsonSerializer.Serialize(receipt, Json));
    }

    internal bool TryValidateReceipt(string? token, string itemId, string role, string originalFileName, string storagePath,
        string sessionId, string formId, HashSet<string> claimedNonces, out CncProtectedReceipt? receipt)
    {
        receipt = null;
        if (!IsValidItemId(itemId) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(storagePath)) return false;
        if (!TryRead(receiptProtector, token, out receipt)) return false;
        return receipt is not null
            && string.Equals(receipt.SessionId, sessionId, StringComparison.Ordinal)
            && string.Equals(receipt.FormId, formId, StringComparison.Ordinal)
            && string.Equals(receipt.ItemId, itemId, StringComparison.Ordinal)
            && string.Equals(receipt.Role, role, StringComparison.Ordinal)
            && string.Equals(receipt.OriginalFileName, originalFileName, StringComparison.Ordinal)
            && string.Equals(receipt.StoragePath, storagePath, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(receipt.Nonce) && receipt.Nonce.Length == 32
            && ValidTime(receipt.IssuedAtUtc, receipt.ExpiresAtUtc) && claimedNonces.Add(receipt.Nonce);
    }

    internal static bool IsValidItemId(string? itemId) => !string.IsNullOrWhiteSpace(itemId) && itemId.Length <= 64
        && itemId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private bool ValidTime(DateTimeOffset issued, DateTimeOffset expires)
    {
        var now = clock.GetUtcNow();
        return issued <= now.AddMinutes(1) && expires > now && expires - issued <= Lifetime;
    }

    private static bool TryRead<T>(IDataProtector protector, string token, out T? value)
    {
        value = default;
        try { value = JsonSerializer.Deserialize<T>(protector.Unprotect(token), Json); return value is not null; }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException) { return false; }
    }
}

internal sealed record CncProtectedForm(string FormId, string SessionId, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc);
internal sealed record CncProtectedReceipt(string SessionId, string FormId, string ItemId, string Role, string OriginalFileName,
    string StoragePath, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc, string Nonce);
