using System.Text.Json;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Legacy.Maliev.Web.Tests;

public sealed class LeadAnalyticsEventQueueParityTests
{
    [Fact]
    public void InstantQuotation_QueuesTheExactSourceWireContract()
    {
        var tempData = CreateTempData();

        Assert.True(LeadAnalyticsEventQueue.TryQueueInstantQuotation(
            tempData,
            "request-44",
            hasFiles: true,
            "11111111-2222-3333-4444-555555555555",
            out var failure));
        Assert.Null(failure);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = document.RootElement;
        Assert.Equal(
            ["event", "has_files", "journey_id", "lead_status", "lead_type", "service", "transaction_id"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("maliev_lead_submitted", root.GetProperty("event").GetString());
        Assert.Equal("instant_3d_quote", root.GetProperty("lead_type").GetString());
        Assert.Equal("3d_printing", root.GetProperty("service").GetString());
        Assert.Equal("request-44", root.GetProperty("transaction_id").GetString());
        Assert.Equal("persisted", root.GetProperty("lead_status").GetString());
        Assert.True(root.GetProperty("has_files").GetBoolean());
        Assert.Equal("11111111-2222-3333-4444-555555555555", root.GetProperty("journey_id").GetString());
    }

    [Fact]
    public void InstantQuotation_RejectsMalformedJourneyWithoutQueueing()
    {
        var tempData = CreateTempData();

        Assert.False(LeadAnalyticsEventQueue.TryQueueInstantQuotation(
            tempData,
            "request-44",
            hasFiles: true,
            "not-a-guid",
            out var failure));

        Assert.IsType<ArgumentException>(failure);
        Assert.False(LeadAnalyticsEventQueue.TryConsume(tempData, out _));
    }

    [Theory]
    [InlineData("3D-Printing", "3d_printing")]
    [InlineData("3d-scanning", "3d_scanning")]
    [InlineData("CNC-Machining", "cnc_machining")]
    public void ManualQuotation_NormalizesTheSourceRouteAndPreservesTheJourney(
        string requestedItem,
        string expectedService)
    {
        var tempData = CreateTempData();

        Assert.True(LeadAnalyticsEventQueue.TryQueueManualQuotation(
            tempData,
            "request-45",
            hasFiles: false,
            requestedItem: requestedItem,
            journeyId: "11111111-2222-3333-4444-555555555555",
            failure: out var failure));
        Assert.Null(failure);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));

        Assert.Equal("manual_quote", payload!.LeadType);
        Assert.Equal(expectedService, payload.Service);
        Assert.Equal("request-45", payload.TransactionId);
        Assert.Equal("11111111-2222-3333-4444-555555555555", payload.JourneyId);
    }

    [Fact]
    public void InstantCncQuotation_UsesDedicatedLeadAndApiTransaction()
    {
        var tempData = CreateTempData();

        Assert.True(LeadAnalyticsEventQueue.TryQueueInstantCncQuotation(
            tempData,
            "request-47",
            hasFiles: true,
            "11111111-2222-3333-4444-555555555555",
            out var failure));
        Assert.Null(failure);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));
        Assert.Equal("instant_cnc_quote", payload!.LeadType);
        Assert.Equal("cnc_machining", payload.Service);
        Assert.Equal("request-47", payload.TransactionId);
    }

    [Fact]
    public void ManualQuotationWithFinder_PreservesValidatedAttribution()
    {
        Assert.True(ServiceFinderAttribution.TryCreate(
            "files-3d",
            "service-3d",
            "material-plastic",
            "quantity-1-10",
            "use-prototype",
            "printing",
            "printing",
            "performance-strength",
            "environment-indoor",
            out var attribution));
        var tempData = CreateTempData();

        Assert.True(LeadAnalyticsEventQueue.TryQueueManualQuotationWithFinder(
            tempData,
            "request-48",
            hasFiles: false,
            requestedItem: "3d-printing",
            journeyId: "11111111-2222-3333-4444-555555555555",
            attribution!,
            out var failure));
        Assert.Null(failure);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));
        Assert.Equal("request-48", payload!.TransactionId);
        Assert.Equal("service-3d", payload.Intent);
        Assert.Equal("printing", payload.FinderPath);
    }

    [Fact]
    public void ContactMessage_QueuesDiagnosticAndNeverProjectsQuotationConversion()
    {
        var tempData = CreateTempData();
        Assert.True(LeadAnalyticsEventQueue.TryQueueContactMessage(tempData, 42, out var failure));
        Assert.Null(failure);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));
        Assert.NotNull(payload);
        Assert.Equal("maliev_contact_submitted", payload.Event);
        Assert.Equal("contact", payload.LeadType);
        Assert.False(payload.IsPersistedQuotation);
        Assert.Throws<InvalidOperationException>(() => payload.BuildGenerateLeadConversionEvent());
        Assert.False(LeadAnalyticsEventQueue.TryConsume(tempData, out _));
    }

    [Fact]
    public void LegacyContactReplay_IsConsumedAsDiagnosticRatherThanConversion()
    {
        var tempData = CreateTempData();
        tempData["Maliev.LeadAnalyticsEvent"] = JsonSerializer.Serialize(new
        {
            @event = "maliev_lead_submitted",
            lead_type = "contact",
            service = "general_contact",
            transaction_id = "message-42",
            lead_status = "persisted",
            has_files = false,
        });

        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));
        Assert.NotNull(payload);
        Assert.Equal("maliev_contact_submitted", payload.Event);
        Assert.False(payload.IsPersistedQuotation);
        Assert.Throws<InvalidOperationException>(() => payload.BuildGenerateLeadConversionEvent());
        Assert.False(LeadAnalyticsEventQueue.TryConsume(tempData, out _));
    }

    [Theory]
    [InlineData("manual_quote", "cnc_machining", "request-42", null, true)]
    [InlineData("instant_3d_quote", "3d_printing", "request-42", null, true)]
    [InlineData("instant_cnc_quote", "cnc_machining", "request-42", null, true)]
    [InlineData("contact", "general_contact", "message-42", null, false)]
    [InlineData("manual_quote", "general_contact", "request-42", null, false)]
    [InlineData("manual_quote", "cnc_machining", "message-42", null, false)]
    [InlineData("manual_quote", "cnc_machining", "request-42", "not-a-guid", false)]
    [InlineData("instant_3d_quote", "3d_scanning", "request-42", null, false)]
    [InlineData("instant_3d_quote", "3d_printing", "request-0", null, false)]
    [InlineData("instant_cnc_quote", "cnc_machining", "request-0", null, false)]
    public void OnlyValidatedPersistedQuotationTuplesCanProjectConversion(
        string leadType, string service, string transactionId, string? journeyId, bool expected)
    {
        var payload = new LeadAnalyticsEvent(
            leadType, service, transactionId, true, null, null, journeyId);
        Assert.Equal(expected, payload.IsPersistedQuotation);
        if (!expected)
        {
            Assert.Throws<InvalidOperationException>(() => payload.BuildGenerateLeadConversionEvent());
            return;
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload.BuildGenerateLeadConversionEvent()));
        Assert.Equal("generate_lead", document.RootElement.GetProperty("event").GetString());
        Assert.Equal(leadType, document.RootElement.GetProperty("lead_type").GetString());
        Assert.Equal(transactionId, document.RootElement.GetProperty("transaction_id").GetString());
    }

    private static TempDataDictionary CreateTempData() =>
        new(new DefaultHttpContext(), new DictionaryTempDataProvider());

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
