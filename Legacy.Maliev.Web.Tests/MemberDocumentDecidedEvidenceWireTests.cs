using System.Net;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.TestHost;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual member receipt/SSR boundaries over synthetic trusted42 session; no production authority join.</summary>
public sealed class MemberDocumentDecidedEvidenceWireTests
{
    [Theory]
    [InlineData("Verified", "missing-actor")]
    [InlineData("Rejected", "missing-actor")]
    [InlineData("Verified", "blank-actor")]
    [InlineData("Rejected", "blank-actor")]
    [InlineData("Verified", "missing-time")]
    [InlineData("Rejected", "missing-time")]
    [InlineData("Verified", "default-time")]
    [InlineData("Rejected", "default-time")]
    [InlineData("Verified", "non-utc")]
    [InlineData("Rejected", "non-utc")]
    [InlineData("Verified", "unknown-kind")]
    [InlineData("Rejected", "null-payload")]
    public async Task MalformedDecidedReceiptIsUnavailableWithoutProtectedMetadata(string status, string fault)
    {
        var document = Guid.NewGuid(); var version = Guid.NewGuid(); var (actor, time) = Evidence(status, fault);
        var receipt = fault == "null-payload" ? null : new CustomerDocumentReceipt(document, version, 42, fault == "unknown-kind" ? "0" : "Evidence", new string('a', 64), 21, [11], status, actor, time, 7);
        var registry = new MemberDocumentsHttpTests.RecordingRegistry { ReceiptValue = receipt };
        await using var app = await MemberDocumentsHttpTests.HostAsync(registry); using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var response = await http.GetAsync($"/member/documents/{document:D}/versions/{version:D}/receipt?customerId=999");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Equal(42, registry.CustomerId);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(document.ToString("D"), body); AssertHidden(body);
    }

    [Theory]
    [InlineData("Verified")]
    [InlineData("Rejected")]
    [InlineData("PendingVerification")]
    public async Task CompleteReceiptProjectsOnlySafeMemberEvidence(string status)
    {
        var document = Guid.NewGuid(); var version = Guid.NewGuid(); var (actor, time) = Evidence(status, "complete");
        var registry = new MemberDocumentsHttpTests.RecordingRegistry { ReceiptValue = new(document, version, 42, "Evidence", new string('a', 64), 21, [11], status, actor, time, 7) };
        await using var app = await MemberDocumentsHttpTests.HostAsync(registry); using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var response = await http.GetAsync($"/member/documents/{document:D}/versions/{version:D}/receipt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(42, registry.CustomerId);
        var body = await response.Content.ReadAsStringAsync(); Assert.Contains(document.ToString("D"), body); Assert.Contains(status, body); AssertHidden(body);
    }

    [Theory]
    [InlineData("en", "Verified", "default-time")]
    [InlineData("th", "Verified", "default-time")]
    [InlineData("en", "Rejected", "missing-actor")]
    [InlineData("th", "Rejected", "missing-actor")]
    [InlineData("en", "Rejected", "missing-time")]
    [InlineData("th", "Rejected", "missing-time")]
    [InlineData("en", "Rejected", "default-time")]
    [InlineData("th", "Rejected", "default-time")]
    [InlineData("en", "Rejected", "non-utc")]
    [InlineData("th", "Rejected", "non-utc")]
    public async Task RenderedHistoryRefusesIncompleteDecidedEvidence(string culture, string status, string fault)
    {
        var document = Guid.NewGuid(); var (actor, time) = Evidence(status, fault);
        var value = new CustomerDocumentVersionSummary(document, Guid.NewGuid(), 1, "Evidence", new string('a', 64), DateTimeOffset.UtcNow, status, actor, time, 7);
        var registry = new MemberDocumentsHttpTests.RecordingRegistry { Summary = new(document, 42, "Evidence", "protected-document-title", "Customer", 9), VersionValues = [value] };
        await using var app = await MemberDocumentsHttpTests.HostAsync(registry); using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var response = await http.GetAsync("/fixture/page?culture=" + culture); var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("role=\"alert\"", html); Assert.DoesNotContain("protected-document-title", html); Assert.DoesNotContain("/download", html); AssertHidden(html);
        Assert.False(CustomerDocumentContractGuard.Version(value, document, "Evidence"));
    }

    [Theory]
    [InlineData("en", "Verified")]
    [InlineData("th", "Verified")]
    [InlineData("en", "Rejected")]
    [InlineData("th", "Rejected")]
    [InlineData("en", "PendingVerification")]
    [InlineData("th", "PendingVerification")]
    public async Task CompleteHistoryRendersWithoutStaffEvidenceMetadata(string culture, string status)
    {
        var document = Guid.NewGuid(); var (actor, time) = Evidence(status, "complete");
        var value = new CustomerDocumentVersionSummary(document, Guid.NewGuid(), 1, "Evidence", new string('a', 64), DateTimeOffset.UtcNow, status, actor, time, 7);
        var registry = new MemberDocumentsHttpTests.RecordingRegistry { Summary = new(document, 42, "Evidence", "safe-document-title", "Customer", 9), VersionValues = [value] };
        await using var app = await MemberDocumentsHttpTests.HostAsync(registry); using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Member", "yes");
        using var response = await http.GetAsync("/fixture/page?culture=" + culture); response.EnsureSuccessStatusCode(); var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("safe-document-title", html); Assert.Contains("/download", html); AssertHidden(html); Assert.True(CustomerDocumentContractGuard.Version(value, document, "Evidence"));
    }

    private static (string? Actor, DateTimeOffset? Time) Evidence(string status, string fault)
    {
        if (status == "PendingVerification") return (null, null);
        string? actor = fault == "missing-actor" ? null : fault == "blank-actor" ? " " : "private-staff-subject";
        DateTimeOffset? time = fault == "missing-time" ? null : fault == "default-time" ? default(DateTimeOffset) : fault == "non-utc" ? new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(7)) : new DateTimeOffset(2026, 10, 8, 5, 0, 0, TimeSpan.Zero);
        return (actor, time);
    }
    private static void AssertHidden(string body) { foreach (var field in new[] { "private-staff-subject", "VerifiedBySubject", "VerifiedAtUtc", "QuotationId", "OrderIds", "CustomerId", "member-secret", "FinancialHistory" }) Assert.DoesNotContain(field, body, StringComparison.OrdinalIgnoreCase); }
}
