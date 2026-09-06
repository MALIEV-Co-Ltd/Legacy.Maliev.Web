using System.Text.Json;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.DataProtection;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncProtectedUploadBindingsTests
{
    [Fact]
    public void Form_BindsSessionAndRejectsTamperingAndExpiry()
    {
        var clock = new Clock();
        var provider = new EphemeralDataProtectionProvider();
        var binding = new CncProtectedUploadBindings(provider, clock);
        var token = binding.CreateFormToken("session");
        Assert.True(binding.TryValidateForm(token, "session", out var form));
        Assert.NotNull(form);
        Assert.False(binding.TryValidateForm(token, "other", out _));
        Assert.False(binding.TryValidateForm("tampered" + token, "session", out _));
        Assert.NotEqual(token, binding.CreateFormToken("session"));
        using var json = JsonDocument.Parse(provider.CreateProtector("Maliev.Web.CncQuotationForm.v1").Unprotect(token));
        Assert.Equal("session", json.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal(form.FormId, json.RootElement.GetProperty("formId").GetString());
        clock.Now = clock.Now.AddHours(3);
        Assert.False(binding.TryValidateForm(token, "session", out _));
    }

    [Theory]
    [InlineData("other", "form", "item", "model", "part.step", "d/s/part.step")]
    [InlineData("session", "other", "item", "model", "part.step", "d/s/part.step")]
    [InlineData("session", "form", "other", "model", "part.step", "d/s/part.step")]
    [InlineData("session", "form", "item", "drawing", "part.step", "d/s/part.step")]
    [InlineData("session", "form", "item", "model", "other.step", "d/s/part.step")]
    [InlineData("session", "form", "item", "model", "part.step", "other/path")]
    public void Receipt_RejectsAnyDifferentBinding(string session, string form, string item, string role, string name, string path)
    {
        var binding = new CncProtectedUploadBindings(new EphemeralDataProtectionProvider(), new Clock());
        var token = binding.CreateReceiptToken("session", "form", "item", "model", "part.step", "d/s/part.step");
        Assert.False(binding.TryValidateReceipt(token, item, role, name, path, session, form, [], out _));
    }

    [Fact]
    public void Receipt_RejectsRepeatedNonceTamperingAndExpiredReceipt()
    {
        var clock = new Clock();
        var binding = new CncProtectedUploadBindings(new EphemeralDataProtectionProvider(), clock);
        var token = binding.CreateReceiptToken("session", "form", "item", "model", "part.step", "d/s/part.step");
        HashSet<string> nonces = [];
        Assert.True(binding.TryValidateReceipt(token, "item", "model", "part.step", "d/s/part.step", "session", "form", nonces, out _));
        Assert.False(binding.TryValidateReceipt(token, "item", "model", "part.step", "d/s/part.step", "session", "form", nonces, out _));
        Assert.False(binding.TryValidateReceipt("tampered" + token, "item", "model", "part.step", "d/s/part.step", "session", "form", [], out _));
        clock.Now = clock.Now.AddHours(3);
        Assert.False(binding.TryValidateReceipt(token, "item", "model", "part.step", "d/s/part.step", "session", "form", [], out _));
    }

    [Theory]
    [InlineData(61, 3600)]
    [InlineData(0, 10801)]
    public void Form_RejectsFutureIssueAndExcessiveLifetime(int issuedOffset, int expiresOffset)
    {
        var provider = new EphemeralDataProtectionProvider();
        var clock = new Clock();
        var token = provider.CreateProtector("Maliev.Web.CncQuotationForm.v1").Protect(JsonSerializer.Serialize(new
        {
            formId = Guid.NewGuid().ToString("N"),
            sessionId = "session",
            issuedAtUtc = clock.Now.AddSeconds(issuedOffset),
            expiresAtUtc = clock.Now.AddSeconds(expiresOffset),
        }));
        Assert.False(new CncProtectedUploadBindings(provider, clock).TryValidateForm(token, "session", out _));
    }

    [Theory]
    [InlineData(61, 3600)]
    [InlineData(0, 10801)]
    public void Receipt_RejectsFutureIssueAndExcessiveLifetime(int issuedOffset, int expiresOffset)
    {
        var provider = new EphemeralDataProtectionProvider();
        var clock = new Clock();
        var token = provider.CreateProtector("Maliev.Web.CncUploadReceipt.v1").Protect(JsonSerializer.Serialize(new
        {
            sessionId = "session",
            formId = "form",
            itemId = "item",
            role = "model",
            originalFileName = "part.step",
            storagePath = "d/s/part.step",
            issuedAtUtc = clock.Now.AddSeconds(issuedOffset),
            expiresAtUtc = clock.Now.AddSeconds(expiresOffset),
            nonce = Guid.NewGuid().ToString("N"),
        }));
        HashSet<string> nonces = [];
        Assert.False(new CncProtectedUploadBindings(provider, clock).TryValidateReceipt(token,
            "item", "model", "part.step", "d/s/part.step", "session", "form", nonces, out _));
        Assert.Empty(nonces);
    }

    [Theory]
    [InlineData("part-123_ABC", true)]
    [InlineData("กข123", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("../part", false)]
    [InlineData("part.step", false)]
    public void ItemId_PreservesSourceCharacterRules(string itemId, bool expected)
    {
        Assert.Equal(expected, CncProtectedUploadBindings.IsValidItemId(itemId));
        Assert.True(CncProtectedUploadBindings.IsValidItemId(new string('a', 64)));
        Assert.False(CncProtectedUploadBindings.IsValidItemId(new string('a', 65)));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
