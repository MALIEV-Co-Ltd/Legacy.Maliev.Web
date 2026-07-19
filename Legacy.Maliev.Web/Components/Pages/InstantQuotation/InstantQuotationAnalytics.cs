using Legacy.Maliev.Web.Application;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public interface IInstantQuotationAnalyticsTracker
{
    ValueTask RecordUploadFailureAsync(
        string operationId,
        InstantQuotationProblemCategory category,
        int fileCount,
        CancellationToken cancellationToken = default);

    ValueTask RecordEstimateShownAsync(
        long revision,
        CancellationToken cancellationToken = default);

    ValueTask RecordReviewReachedAsync(
        long revision,
        CancellationToken cancellationToken = default);
}

public interface IInstantQuotationAnalyticsSink
{
    ValueTask EmitAsync(
        InstantQuotationAnalyticsPayload payload,
        CancellationToken cancellationToken = default);
}

public abstract class InstantQuotationAnalyticsPayload
{
    private protected InstantQuotationAnalyticsPayload(string eventName)
    {
        Event = eventName;
    }

    [JsonPropertyName("event")]
    public string Event { get; }

    [JsonPropertyName("service")]
    public string Service => "3d_printing";
}

public sealed class InstantQuotationUploadFailurePayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationUploadFailurePayload(string failureCategory, int fileCount)
        : base("upload_failure")
    {
        FailureCategory = failureCategory;
        FileCount = fileCount;
    }

    [JsonPropertyName("failure_category")]
    public string FailureCategory { get; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; }
}

public sealed class InstantQuotationEstimateShownPayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationEstimateShownPayload()
        : base("estimate_shown")
    {
    }
}

public sealed class InstantQuotationReviewReachedPayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationReviewReachedPayload()
        : base("review_reached")
    {
    }
}

public sealed class NoOpInstantQuotationAnalyticsTracker : IInstantQuotationAnalyticsTracker
{
    private NoOpInstantQuotationAnalyticsTracker()
    {
    }

    public static NoOpInstantQuotationAnalyticsTracker Instance { get; } = new();

    public ValueTask RecordUploadFailureAsync(
        string operationId,
        InstantQuotationProblemCategory category,
        int fileCount,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask RecordEstimateShownAsync(
        long revision,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask RecordReviewReachedAsync(
        long revision,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

public sealed class InstantQuotationAnalyticsTracker : IInstantQuotationAnalyticsTracker
{
    private readonly IInstantQuotationAnalyticsSink sink;
    private readonly Lock synchronization = new();
    private readonly HashSet<string> uploadFailureOperations = new(StringComparer.Ordinal);
    private readonly HashSet<long> estimateRevisions = [];
    private readonly HashSet<long> reviewRevisions = [];

    public InstantQuotationAnalyticsTracker(IInstantQuotationAnalyticsSink sink)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public ValueTask RecordUploadFailureAsync(
        string operationId,
        InstantQuotationProblemCategory category,
        int fileCount,
        CancellationToken cancellationToken = default)
    {
        var failureCategory = MapFailureCategory(category);
        if (string.IsNullOrWhiteSpace(operationId)
            || failureCategory is null
            || fileCount != 1
            || !TryReserve(uploadFailureOperations, operationId))
        {
            return ValueTask.CompletedTask;
        }

        return EmitWithoutBreakingFlowAsync(
            new InstantQuotationUploadFailurePayload(failureCategory, fileCount),
            cancellationToken);
    }

    public ValueTask RecordEstimateShownAsync(
        long revision,
        CancellationToken cancellationToken = default)
    {
        if (revision <= 0 || !TryReserve(estimateRevisions, revision))
        {
            return ValueTask.CompletedTask;
        }

        return EmitWithoutBreakingFlowAsync(
            new InstantQuotationEstimateShownPayload(),
            cancellationToken);
    }

    public ValueTask RecordReviewReachedAsync(
        long revision,
        CancellationToken cancellationToken = default)
    {
        if (revision <= 0 || !TryReserve(reviewRevisions, revision))
        {
            return ValueTask.CompletedTask;
        }

        return EmitWithoutBreakingFlowAsync(
            new InstantQuotationReviewReachedPayload(),
            cancellationToken);
    }

    private bool TryReserve<T>(HashSet<T> values, T value)
    {
        lock (synchronization)
        {
            return values.Add(value);
        }
    }

    private async ValueTask EmitWithoutBreakingFlowAsync(
        InstantQuotationAnalyticsPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await sink.EmitAsync(payload, cancellationToken);
        }
        catch (Exception)
        {
            // Analytics is best-effort and must never interrupt the quotation workflow.
        }
    }

    private static string? MapFailureCategory(InstantQuotationProblemCategory category) => category switch
    {
        InstantQuotationProblemCategory.Validation => "validation",
        InstantQuotationProblemCategory.Authorization => "authorization",
        InstantQuotationProblemCategory.Conflict => "conflict",
        InstantQuotationProblemCategory.DependencyUnavailable => "dependency_unavailable",
        InstantQuotationProblemCategory.Unexpected => "unexpected",
        _ => null,
    };
}
