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
            44,
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
        Assert.Equal("quotation-44", root.GetProperty("transaction_id").GetString());
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
            44,
            hasFiles: true,
            "not-a-guid",
            out var failure));

        Assert.IsType<ArgumentException>(failure);
        Assert.False(LeadAnalyticsEventQueue.TryConsume(tempData, out _));
    }

    [Theory]
    [InlineData("3D-Printing", "3d_printing")]
    [InlineData("3d-scanning", "3d_scanning")]
    [InlineData("CNC-Machining", "custom_manufacturing")]
    public void ManualQuotation_NormalizesTheSourceRouteAndPreservesTheJourney(
        string requestedItem,
        string expectedService)
    {
        var tempData = CreateTempData();

        Assert.True(LeadAnalyticsEventQueue.TryQueueManualQuotation(
            tempData,
            45,
            hasFiles: false,
            requestedItem: requestedItem,
            journeyId: "11111111-2222-3333-4444-555555555555",
            failure: out var failure));
        Assert.Null(failure);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));

        Assert.Equal("manual_quote", payload!.LeadType);
        Assert.Equal(expectedService, payload.Service);
        Assert.Equal("11111111-2222-3333-4444-555555555555", payload.JourneyId);
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
