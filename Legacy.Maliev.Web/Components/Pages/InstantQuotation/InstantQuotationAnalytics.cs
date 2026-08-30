using Legacy.Maliev.Web.Application;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public interface IInstantQuotationAnalyticsTracker
{
    ValueTask RecordStageResultAsync(
        InstantQuotationStage stage,
        InstantQuotationStageResult result,
        InstantQuotationStageFailure? failure,
        string? fileType,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    ValueTask RecordUploadStartAsync(
        string batchId,
        int fileCount,
        CancellationToken cancellationToken = default);

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

public enum InstantQuotationStage
{
    FileValidation,
    Parse,
    Upload,
    Thumbnail,
    Pricing,
    OrderTotal,
}

public enum InstantQuotationStageResult
{
    Success,
    Failure,
}

public enum InstantQuotationStageFailure
{
    UnsupportedType,
    FileTooLarge,
    UnreadableModel,
    SnapshotUnavailable,
    ServerRejected,
    Network,
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

public sealed class InstantQuotationUploadStartPayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationUploadStartPayload(int fileCount)
        : base("file_upload_start")
    {
        FileCount = fileCount;
    }

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

public sealed class InstantQuotationStageResultPayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationStageResultPayload(
        string stage,
        string result,
        string? failureCategory,
        string? fileType)
        : base("quote_stage_result")
    {
        Stage = stage;
        Result = result;
        FailureCategory = failureCategory;
        FileType = fileType;
    }

    [JsonPropertyName("stage")]
    public string Stage { get; }

    [JsonPropertyName("result")]
    public string Result { get; }

    [JsonPropertyName("failure_category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureCategory { get; }

    [JsonPropertyName("file_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileType { get; }
}

public sealed class NoOpInstantQuotationAnalyticsTracker : IInstantQuotationAnalyticsTracker
{
    private NoOpInstantQuotationAnalyticsTracker()
    {
    }

    public static NoOpInstantQuotationAnalyticsTracker Instance { get; } = new();

    public ValueTask RecordStageResultAsync(
        InstantQuotationStage stage,
        InstantQuotationStageResult result,
        InstantQuotationStageFailure? failure,
        string? fileType,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask RecordUploadStartAsync(
        string batchId,
        int fileCount,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

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
    private readonly HashSet<string> uploadBatches = new(StringComparer.Ordinal);
    private readonly HashSet<string> uploadFailureOperations = new(StringComparer.Ordinal);
    private readonly HashSet<long> estimateRevisions = [];
    private readonly HashSet<long> reviewRevisions = [];
    private readonly HashSet<string> stageResults = new(StringComparer.Ordinal);

    public InstantQuotationAnalyticsTracker(IInstantQuotationAnalyticsSink sink)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public ValueTask RecordStageResultAsync(
        InstantQuotationStage stage,
        InstantQuotationStageResult result,
        InstantQuotationStageFailure? failure,
        string? fileType,
        CancellationToken cancellationToken = default)
    {
        var stageName = MapStage(stage);
        var resultName = MapStageResult(result);
        var failureName = failure is null ? null : MapStageFailure(failure.Value);
        var normalizedFileType = NormalizeFileType(fileType);
        var isValid = stageName is not null
            && resultName is not null
            && (result is InstantQuotationStageResult.Success
                ? failure is null
                : failureName is not null)
            && (fileType is null || normalizedFileType is not null);
        if (!isValid)
        {
            return ValueTask.CompletedTask;
        }

        var dedupeKey = string.Join('|', stageName, resultName, failureName, normalizedFileType);
        if (!TryReserve(stageResults, dedupeKey))
        {
            return ValueTask.CompletedTask;
        }

        return EmitWithoutBreakingFlowAsync(
            new InstantQuotationStageResultPayload(
                stageName!,
                resultName!,
                failureName,
                normalizedFileType),
            cancellationToken);
    }

    public ValueTask RecordUploadStartAsync(
        string batchId,
        int fileCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId)
            || fileCount is <= 0 or > 100
            || !TryReserve(uploadBatches, batchId))
        {
            return ValueTask.CompletedTask;
        }

        return EmitWithoutBreakingFlowAsync(
            new InstantQuotationUploadStartPayload(fileCount),
            cancellationToken);
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

    private static string? MapStage(InstantQuotationStage stage) => stage switch
    {
        InstantQuotationStage.FileValidation => "file_validation",
        InstantQuotationStage.Parse => "parse",
        InstantQuotationStage.Upload => "upload",
        InstantQuotationStage.Thumbnail => "thumbnail",
        InstantQuotationStage.Pricing => "pricing",
        InstantQuotationStage.OrderTotal => "order_total",
        _ => null,
    };

    private static string? MapStageResult(InstantQuotationStageResult result) => result switch
    {
        InstantQuotationStageResult.Success => "success",
        InstantQuotationStageResult.Failure => "failure",
        _ => null,
    };

    private static string? MapStageFailure(InstantQuotationStageFailure failure) => failure switch
    {
        InstantQuotationStageFailure.UnsupportedType => "unsupported_type",
        InstantQuotationStageFailure.FileTooLarge => "file_too_large",
        InstantQuotationStageFailure.UnreadableModel => "unreadable_model",
        InstantQuotationStageFailure.SnapshotUnavailable => "snapshot_unavailable",
        InstantQuotationStageFailure.ServerRejected => "server_rejected",
        InstantQuotationStageFailure.Network => "network",
        _ => null,
    };

    private static string? NormalizeFileType(string? fileType)
    {
        if (fileType is null)
        {
            return null;
        }

        var normalized = fileType.Trim().ToLowerInvariant();
        return normalized.Length is > 0 and <= 10
            && normalized.All(static character => char.IsAsciiLetterOrDigit(character))
                ? normalized
                : null;
    }
}
