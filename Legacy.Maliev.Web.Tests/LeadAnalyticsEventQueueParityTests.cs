using System.Text.Json;
using Microsoft.Extensions.Logging;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using LegacyQueue = Legacy.Maliev.Web.Tests.Pre605.LeadAnalyticsEventQueue;

namespace Legacy.Maliev.Web.Tests;

public sealed class LeadAnalyticsEventQueueParityTests
{
    [Fact]
    public void ProducerValidationAndActualTempDataSetterFailuresAreReturnedWithoutEscaping()
    {
        var invalid = new ThrowingTempDataDictionary();
        Assert.False(LeadAnalyticsEventQueue.TryQueueContactMessage(invalid, 0, out var invalidFailure));
        Assert.IsType<ArgumentOutOfRangeException>(invalidFailure);
        Assert.Equal(0, invalid.SetterCalls);

        var contact = new ThrowingTempDataDictionary();
        Assert.False(LeadAnalyticsEventQueue.TryQueueContactMessage(contact, 42, out var contactFailure));
        Assert.Same(contact.WriteFailure, contactFailure);
        Assert.Equal(1, contact.SetterCalls);
        Assert.Equal("Maliev.ContactAnalyticsEvent.v1", contact.AttemptedKey);
        using var contactPayload = JsonDocument.Parse(Assert.IsType<string>(contact.AttemptedValue));
        Assert.Equal("maliev_contact_submitted", contactPayload.RootElement.GetProperty("event").GetString());
        Assert.Equal("message-42", contactPayload.RootElement.GetProperty("transaction_id").GetString());

        var quotation = new ThrowingTempDataDictionary();
        Assert.False(LeadAnalyticsEventQueue.TryQueueManualQuotation(
            quotation, "request-9043", true, "3d-printing",
            "11111111-2222-3333-4444-555555555555", out var quotationFailure));
        Assert.Same(quotation.WriteFailure, quotationFailure);
        Assert.Equal(1, quotation.SetterCalls);
        Assert.Equal("Maliev.LeadAnalyticsEvent", quotation.AttemptedKey);
        using var quotationPayload = JsonDocument.Parse(Assert.IsType<string>(quotation.AttemptedValue));
        Assert.Equal("request-9043", quotationPayload.RootElement.GetProperty("transaction_id").GetString());
        Assert.Equal("11111111-2222-3333-4444-555555555555",
            quotationPayload.RootElement.GetProperty("journey_id").GetString());
        Assert.True(quotationPayload.RootElement.GetProperty("has_files").GetBoolean());
        Assert.Equal(0, contact.GetterCalls + quotation.GetterCalls);
        Assert.Equal(0, contact.SaveCalls + quotation.SaveCalls);
    }

    [Fact]
    public void Pre605ContactProducerAndConsumer_ReproducesLegacyConversion()
    {
        var tempData = CreateTempData();
        Assert.True(LegacyQueue.TryQueueContactMessage(tempData, 42, out var failure));
        Assert.Null(failure);
        Assert.True(LegacyQueue.TryConsume(tempData, out var payload));
        Assert.Equal("maliev_lead_submitted", payload!.Event);
        using var conversion = JsonDocument.Parse(JsonSerializer.Serialize(payload.BuildGenerateLeadConversionEvent()));
        Assert.Equal("generate_lead", conversion.RootElement.GetProperty("event").GetString());
    }

    [Fact]
    public void CurrentContactProducer_LegacyGetCannotConsumeOrConvert_NewGetConsumesOnce()
    {
        var provider = new RoundTripTempDataProvider();
        var post = new TempDataDictionary(new DefaultHttpContext(), provider);
        Assert.True(LeadAnalyticsEventQueue.TryQueueContactMessage(post, 42, out var failure));
        Assert.Null(failure);
        post.Save();

        var legacyGet = new TempDataDictionary(new DefaultHttpContext(), provider);
        Assert.False(LegacyQueue.TryConsume(legacyGet, out var legacyPayload));
        Assert.Null(legacyPayload);
        legacyGet.Save();

        var currentGet = new TempDataDictionary(new DefaultHttpContext(), provider);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(currentGet, out var payload));
        Assert.Equal("maliev_contact_submitted", payload!.Event);
        Assert.False(payload.IsPersistedQuotation);
        Assert.Throws<InvalidOperationException>(() => payload.BuildGenerateLeadConversionEvent());
        currentGet.Save();
        Assert.False(LeadAnalyticsEventQueue.TryConsume(
            new TempDataDictionary(new DefaultHttpContext(), provider), out _));
    }

    [Fact]
    public void LegacyContactProducer_CurrentConsumerConvertsOnlyToDiagnostic()
    {
        var tempData = CreateTempData();
        Assert.True(LegacyQueue.TryQueueContactMessage(tempData, 42, out var failure));
        Assert.Null(failure);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));
        Assert.Equal("maliev_contact_submitted", payload!.Event);
        Assert.False(payload.IsPersistedQuotation);
        Assert.Throws<InvalidOperationException>(() => payload.BuildGenerateLeadConversionEvent());
        Assert.False(LeadAnalyticsEventQueue.TryConsume(tempData, out _));
    }

    [Theory]
    [InlineData("manual_quote", "cnc_machining")]
    [InlineData("instant_3d_quote", "3d_printing")]
    [InlineData("instant_cnc_quote", "cnc_machining")]
    public void CurrentQuotationProducer_LegacyConsumerPreservesConversion(string leadType, string service)
    {
        var tempData = CreateTempData();
        var journeyId = "11111111-2222-3333-4444-555555555555";
        var queued = leadType switch
        {
            "manual_quote" => LeadAnalyticsEventQueue.TryQueueManualQuotation(tempData, "request-42", true, "cnc-machining", journeyId, out _),
            "instant_3d_quote" => LeadAnalyticsEventQueue.TryQueueInstantQuotation(tempData, "request-42", true, journeyId, out _),
            _ => LeadAnalyticsEventQueue.TryQueueInstantCncQuotation(tempData, "request-42", true, journeyId, out _),
        };

        Assert.True(queued);
        Assert.True(LegacyQueue.TryConsume(tempData, out var payload));
        using var conversion = JsonDocument.Parse(JsonSerializer.Serialize(payload!.BuildGenerateLeadConversionEvent()));
        Assert.Equal("generate_lead", conversion.RootElement.GetProperty("event").GetString());
        Assert.Equal(leadType, conversion.RootElement.GetProperty("lead_type").GetString());
        Assert.Equal(service, conversion.RootElement.GetProperty("service").GetString());
        Assert.Equal("request-42", conversion.RootElement.GetProperty("transaction_id").GetString());
        Assert.Equal(journeyId, conversion.RootElement.GetProperty("journey_id").GetString());
        Assert.False(LegacyQueue.TryConsume(tempData, out _));
    }

    [Theory]
    [InlineData("manual_quote", "cnc_machining")]
    [InlineData("instant_3d_quote", "3d_printing")]
    [InlineData("instant_cnc_quote", "cnc_machining")]
    public void LegacyQuotationProducer_CurrentConsumerPreservesConversion(string leadType, string service)
    {
        var tempData = CreateTempData();
        var journeyId = "11111111-2222-3333-4444-555555555555";
        var queued = leadType switch
        {
            "manual_quote" => LegacyQueue.TryQueueManualQuotation(tempData, "request-42", true, "cnc-machining", journeyId, out _),
            "instant_3d_quote" => LegacyQueue.TryQueueInstantQuotation(tempData, "request-42", true, journeyId, out _),
            _ => LegacyQueue.TryQueueInstantCncQuotation(tempData, "request-42", true, journeyId, out _),
        };

        Assert.True(queued);
        Assert.True(LeadAnalyticsEventQueue.TryConsume(tempData, out var payload));
        Assert.True(payload!.IsPersistedQuotation);
        using var conversion = JsonDocument.Parse(JsonSerializer.Serialize(payload.BuildGenerateLeadConversionEvent()));
        Assert.Equal("generate_lead", conversion.RootElement.GetProperty("event").GetString());
        Assert.Equal(leadType, conversion.RootElement.GetProperty("lead_type").GetString());
        Assert.Equal(service, conversion.RootElement.GetProperty("service").GetString());
        Assert.Equal("request-42", conversion.RootElement.GetProperty("transaction_id").GetString());
        Assert.Equal(journeyId, conversion.RootElement.GetProperty("journey_id").GetString());
        Assert.False(LeadAnalyticsEventQueue.TryConsume(tempData, out _));
    }

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

    private sealed class RoundTripTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> persisted = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(this.persisted);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
            this.persisted = new Dictionary<string, object>(values);
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
    internal sealed class ThrowingTempDataDictionary : Dictionary<string, object?>, ITempDataDictionary
    {
        public InvalidOperationException WriteFailure { get; } = new("private-tempdata-setter-fault");
        public int SetterCalls { get; private set; }
        public int GetterCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public string? AttemptedKey { get; private set; }
        public object? AttemptedValue { get; private set; }

        object? IDictionary<string, object?>.this[string key]
        {
            get
            {
                GetterCalls++;
                return TryGetValue(key, out var value) ? value : null;
            }
            set
            {
                SetterCalls++;
                AttemptedKey = key;
                AttemptedValue = value;
                throw WriteFailure;
            }
        }

        public void Keep()
        {
        }

        public void Keep(string key)
        {
        }

        public void Load()
        {
        }
        public object? Peek(string key) => TryGetValue(key, out var value) ? value : null;
        public void Save() => SaveCalls++;
    }

    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
