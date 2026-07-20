using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationAnalyticsCoreTests
{
    [Fact]
    public async Task NoOpTracker_KeepsPendingEventsInactive()
    {
        var tracker = NoOpInstantQuotationAnalyticsTracker.Instance;

        await tracker.RecordUploadStartAsync("batch-1", 1);
        await tracker.RecordUploadFailureAsync(
            "operation-1",
            InstantQuotationProblemCategory.Validation,
            1);
        await tracker.RecordEstimateShownAsync(1);
        await tracker.RecordReviewReachedAsync(1);
    }

    [Fact]
    public async Task UploadStart_EmitsExactApprovedPayloadOncePerAnalyzedBatch()
    {
        var sink = new RecordingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);

        await tracker.RecordUploadStartAsync("batch-1", 2);
        await tracker.RecordUploadStartAsync("batch-1", 2);
        await tracker.RecordUploadStartAsync("batch-2", 1);

        Assert.Equal(2, sink.Payloads.Count);
        AssertJson(sink.Payloads[0], new Dictionary<string, object>
        {
            ["event"] = "file_upload_start",
            ["service"] = "3d_printing",
            ["file_count"] = 2,
        });
        AssertJson(sink.Payloads[1], new Dictionary<string, object>
        {
            ["event"] = "file_upload_start",
            ["service"] = "3d_printing",
            ["file_count"] = 1,
        });
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("batch-1", 0)]
    [InlineData("batch-1", -1)]
    [InlineData("batch-1", 101)]
    public async Task UploadStart_InvalidContractInputFailsClosed(string? batchId, int fileCount)
    {
        var sink = new RecordingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);

        await tracker.RecordUploadStartAsync(batchId!, fileCount);

        Assert.Empty(sink.Payloads);
    }

    [Theory]
    [InlineData(InstantQuotationProblemCategory.Validation, "validation")]
    [InlineData(InstantQuotationProblemCategory.Authorization, "authorization")]
    [InlineData(InstantQuotationProblemCategory.Conflict, "conflict")]
    [InlineData(InstantQuotationProblemCategory.DependencyUnavailable, "dependency_unavailable")]
    [InlineData(InstantQuotationProblemCategory.Unexpected, "unexpected")]
    public async Task UploadFailure_EmitsExactApprovedPayload(
        InstantQuotationProblemCategory category,
        string expectedCategory)
    {
        var sink = new RecordingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);

        await tracker.RecordUploadFailureAsync("operation-1", category, 1);

        var payload = Assert.Single(sink.Payloads);
        AssertJson(payload, new Dictionary<string, object>
        {
            ["event"] = "upload_failure",
            ["service"] = "3d_printing",
            ["failure_category"] = expectedCategory,
            ["file_count"] = 1,
        });
    }

    [Fact]
    public async Task UploadFailure_DeduplicatesOneTerminalFailurePerOperation()
    {
        var sink = new RecordingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);

        await tracker.RecordUploadFailureAsync("operation-1", InstantQuotationProblemCategory.Validation, 1);
        await tracker.RecordUploadFailureAsync("operation-1", InstantQuotationProblemCategory.Validation, 1);
        await tracker.RecordUploadFailureAsync("operation-2", InstantQuotationProblemCategory.Validation, 1);

        Assert.Equal(2, sink.Payloads.Count);
    }

    [Theory]
    [InlineData(null, InstantQuotationProblemCategory.Validation, 1)]
    [InlineData("", InstantQuotationProblemCategory.Validation, 1)]
    [InlineData("operation-1", InstantQuotationProblemCategory.None, 1)]
    [InlineData("operation-1", (InstantQuotationProblemCategory)999, 1)]
    [InlineData("operation-1", InstantQuotationProblemCategory.Validation, 0)]
    [InlineData("operation-1", InstantQuotationProblemCategory.Validation, -1)]
    [InlineData("operation-1", InstantQuotationProblemCategory.Validation, 2)]
    public async Task UploadFailure_InvalidContractInputFailsClosed(
        string? operationId,
        InstantQuotationProblemCategory category,
        int fileCount)
    {
        var sink = new RecordingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);

        await tracker.RecordUploadFailureAsync(operationId!, category, fileCount);

        Assert.Empty(sink.Payloads);
    }

    [Fact]
    public async Task EstimateShown_EmitsExactPayloadOncePerAuthoritativeRevision()
    {
        var sink = new RecordingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);

        await tracker.RecordEstimateShownAsync(7);
        await tracker.RecordEstimateShownAsync(7);
        await tracker.RecordEstimateShownAsync(8);

        Assert.Equal(2, sink.Payloads.Count);
        Assert.All(sink.Payloads, payload => AssertJson(payload, new Dictionary<string, object>
        {
            ["event"] = "estimate_shown",
            ["service"] = "3d_printing",
        }));
    }

    [Fact]
    public async Task ReviewReached_EmitsExactPayloadOncePerAuthoritativeRevision()
    {
        var sink = new RecordingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);

        await tracker.RecordReviewReachedAsync(7);
        await tracker.RecordReviewReachedAsync(7);
        await tracker.RecordReviewReachedAsync(8);

        Assert.Equal(2, sink.Payloads.Count);
        Assert.All(sink.Payloads, payload => AssertJson(payload, new Dictionary<string, object>
        {
            ["event"] = "review_reached",
            ["service"] = "3d_printing",
        }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AuthoritativeRevisionEvents_NonPositiveRevisionFailsClosed(long revision)
    {
        var sink = new RecordingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);

        await tracker.RecordEstimateShownAsync(revision);
        await tracker.RecordReviewReachedAsync(revision);

        Assert.Empty(sink.Payloads);
    }

    [Fact]
    public async Task Tracker_SinkFailureDoesNotBreakQuotationFlow()
    {
        var tracker = new InstantQuotationAnalyticsTracker(new ThrowingSink());

        await tracker.RecordEstimateShownAsync(1);
        await tracker.RecordReviewReachedAsync(1);
        await tracker.RecordUploadFailureAsync(
            "operation-1",
            InstantQuotationProblemCategory.Unexpected,
            1);
    }

    private static void AssertJson(
        InstantQuotationAnalyticsPayload payload,
        IReadOnlyDictionary<string, object> expected)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, payload.GetType()));
        var properties = document.RootElement.EnumerateObject().ToArray();

        Assert.Equal(expected.Keys.Order(), properties.Select(property => property.Name).Order());
        foreach (var property in properties)
        {
            var expectedValue = expected[property.Name];
            if (expectedValue is int expectedInteger)
            {
                Assert.Equal(expectedInteger, property.Value.GetInt32());
            }
            else
            {
                Assert.Equal(expectedValue, property.Value.GetString());
            }
        }
    }

    private sealed class RecordingSink : IInstantQuotationAnalyticsSink
    {
        public List<InstantQuotationAnalyticsPayload> Payloads { get; } = [];

        public ValueTask EmitAsync(
            InstantQuotationAnalyticsPayload payload,
            CancellationToken cancellationToken = default)
        {
            Payloads.Add(payload);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IInstantQuotationAnalyticsSink
    {
        public ValueTask EmitAsync(
            InstantQuotationAnalyticsPayload payload,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("Unavailable analytics runtime."));
    }
}
