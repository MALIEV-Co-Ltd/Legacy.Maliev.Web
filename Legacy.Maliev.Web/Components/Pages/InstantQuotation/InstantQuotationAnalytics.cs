using Legacy.Maliev.Web.Application;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Defines best-effort, privacy-bounded observations for the additive quotation workflow.</summary>
public interface IInstantQuotationAnalyticsTracker
{
    /// <summary>Records a bounded stage outcome; the default implementation performs no delivery.</summary>
    /// <param name="stage">The quotation stage being observed.</param>
    /// <param name="result">Whether the stage succeeded or failed.</param>
    /// <param name="failure">The failure category, or null for a successful outcome.</param>
    /// <param name="fileType">An optional short file extension, not a file name.</param>
    /// <param name="cancellationToken">Token to cancel delivery of the observation.</param>
    /// <returns>A value task representing the observation attempt.</returns>
    ValueTask RecordStageResultAsync(
        InstantQuotationStage stage,
        InstantQuotationStageResult result,
        InstantQuotationStageFailure? failure,
        string? fileType,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>Records that a selected batch has entered the upload workflow.</summary>
    /// <param name="batchId">The local deduplication identifier; it is not an analytics payload field.</param>
    /// <param name="fileCount">The number of selected files in the batch.</param>
    /// <param name="cancellationToken">Token to cancel delivery of the observation.</param>
    /// <returns>A value task representing the observation attempt.</returns>
    ValueTask RecordUploadStartAsync(
        string batchId,
        int fileCount,
        CancellationToken cancellationToken = default);

    /// <summary>Records an upload attempt's bounded failure without emitting its identifier.</summary>
    /// <param name="operationId">The local upload-attempt deduplication key.</param>
    /// <param name="category">The allowlisted upload problem category.</param>
    /// <param name="fileCount">The number of affected files.</param>
    /// <param name="cancellationToken">Token to cancel delivery of the observation.</param>
    /// <returns>A value task representing the observation attempt.</returns>
    ValueTask RecordUploadFailureAsync(
        string operationId,
        InstantQuotationProblemCategory category,
        int fileCount,
        CancellationToken cancellationToken = default);

    /// <summary>Records that a complete estimate for a quote revision is visibly presented.</summary>
    /// <param name="revision">The positive local quote revision used only for deduplication.</param>
    /// <param name="cancellationToken">Token to cancel delivery of the observation.</param>
    /// <returns>A value task representing the observation attempt.</returns>
    ValueTask RecordEstimateShownAsync(
        long revision,
        CancellationToken cancellationToken = default);

    /// <summary>Records that review of a complete quote revision is visibly presented.</summary>
    /// <param name="revision">The positive local quote revision used only for deduplication.</param>
    /// <param name="cancellationToken">Token to cancel delivery of the observation.</param>
    /// <returns>A value task representing the observation attempt.</returns>
    ValueTask RecordReviewReachedAsync(
        long revision,
        CancellationToken cancellationToken = default);
}

/// <summary>Identifies the bounded quotation stage recorded without customer or file identifiers.</summary>
public enum InstantQuotationStage
{
    /// <summary>Validation of selected file type and size.</summary>
    FileValidation,
    /// <summary>Parsing and advisory geometry extraction.</summary>
    Parse,
    /// <summary>Transfer and server admission of the selected file.</summary>
    Upload,
    /// <summary>Creation of a part preview thumbnail.</summary>
    Thumbnail,
    /// <summary>Calculation of the part estimate.</summary>
    Pricing,
    /// <summary>Calculation of the combined order total.</summary>
    OrderTotal,
}

/// <summary>Identifies whether a quotation stage completed successfully or failed.</summary>
public enum InstantQuotationStageResult
{
    /// <summary>The observed stage completed successfully.</summary>
    Success,
    /// <summary>The observed stage failed.</summary>
    Failure,
}

/// <summary>Classifies quotation-stage failures using the closed analytics vocabulary.</summary>
public enum InstantQuotationStageFailure
{
    /// <summary>The selected file type is outside the allowed formats.</summary>
    UnsupportedType,
    /// <summary>The selected file exceeds the upload size limit.</summary>
    FileTooLarge,
    /// <summary>The selected model could not be read for quotation.</summary>
    UnreadableModel,
    /// <summary>A preview snapshot could not be produced.</summary>
    SnapshotUnavailable,
    /// <summary>The server refused the attempted operation.</summary>
    ServerRejected,
    /// <summary>A network or dependency failure interrupted the stage.</summary>
    Network,
}

/// <summary>Delivers a typed quotation analytics payload without controlling quotation success.</summary>
public interface IInstantQuotationAnalyticsSink
{
    /// <summary>Attempts to deliver the typed observation to the configured analytics destination.</summary>
    /// <param name="payload">The closed quotation analytics payload to deliver.</param>
    /// <param name="cancellationToken">Token to cancel delivery of the observation.</param>
    /// <returns>A value task representing delivery.</returns>
    ValueTask EmitAsync(
        InstantQuotationAnalyticsPayload payload,
        CancellationToken cancellationToken = default);
}

/// <summary>Carries the event and service fields shared by privacy-bounded additive analytics.</summary>
public abstract class InstantQuotationAnalyticsPayload
{
    private protected InstantQuotationAnalyticsPayload(string eventName)
    {
        Event = eventName;
    }

    /// <summary>Gets the closed event name serialized for this quotation observation.</summary>
    [JsonPropertyName("event")]
    public string Event { get; }

    /// <summary>Gets the additive-printing service label shared by these payloads.</summary>
    [JsonPropertyName("service")]
    public string Service => "3d_printing";
}

/// <summary>Carries a bounded upload failure category and affected file count, without the upload identifier.</summary>
public sealed class InstantQuotationUploadFailurePayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationUploadFailurePayload(string failureCategory, int fileCount)
        : base("upload_failure")
    {
        FailureCategory = failureCategory;
        FileCount = fileCount;
    }

    /// <summary>Gets the allowlisted upload problem category emitted to analytics.</summary>
    [JsonPropertyName("failure_category")]
    public string FailureCategory { get; }

    /// <summary>Gets the number of files represented by the upload failure.</summary>
    [JsonPropertyName("file_count")]
    public int FileCount { get; }
}

/// <summary>Carries the selected batch size without exposing its local deduplication identifier.</summary>
public sealed class InstantQuotationUploadStartPayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationUploadStartPayload(int fileCount)
        : base("file_upload_start")
    {
        FileCount = fileCount;
    }

    /// <summary>Gets the number of files in the accepted upload batch.</summary>
    [JsonPropertyName("file_count")]
    public int FileCount { get; }
}

/// <summary>Marks a visible, complete quotation estimate without including its amount or revision.</summary>
public sealed class InstantQuotationEstimateShownPayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationEstimateShownPayload()
        : base("estimate_shown")
    {
    }
}

/// <summary>Marks a visible review milestone without transmitting its local quote revision.</summary>
public sealed class InstantQuotationReviewReachedPayload : InstantQuotationAnalyticsPayload
{
    internal InstantQuotationReviewReachedPayload()
        : base("review_reached")
    {
    }
}

/// <summary>Carries a validated stage outcome and optional bounded failure and file-type labels.</summary>
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

    /// <summary>Gets the allowlisted stage label, such as parsing or order-total pricing.</summary>
    [JsonPropertyName("stage")]
    public string Stage { get; }

    /// <summary>Gets the success or failure label for the observed stage.</summary>
    [JsonPropertyName("result")]
    public string Result { get; }

    /// <summary>Gets the bounded failure label, omitted from serialization when absent.</summary>
    [JsonPropertyName("failure_category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureCategory { get; }

    /// <summary>Gets the normalized short file-type label, omitted when no file type applies.</summary>
    [JsonPropertyName("file_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileType { get; }
}

/// <summary>Accepts analytics calls without emitting observations or changing the quotation flow.</summary>
public sealed class NoOpInstantQuotationAnalyticsTracker : IInstantQuotationAnalyticsTracker
{
    private NoOpInstantQuotationAnalyticsTracker()
    {
    }

    /// <summary>Gets the shared disabled tracker used when analytics is not configured.</summary>
    public static NoOpInstantQuotationAnalyticsTracker Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask RecordStageResultAsync(
        InstantQuotationStage stage,
        InstantQuotationStageResult result,
        InstantQuotationStageFailure? failure,
        string? fileType,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask RecordUploadStartAsync(
        string batchId,
        int fileCount,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask RecordUploadFailureAsync(
        string operationId,
        InstantQuotationProblemCategory category,
        int fileCount,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask RecordEstimateShownAsync(
        long revision,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask RecordReviewReachedAsync(
        long revision,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>Validates and deduplicates workflow observations before sending best-effort analytics.</summary>
public sealed class InstantQuotationAnalyticsTracker : IInstantQuotationAnalyticsTracker
{
    private readonly IInstantQuotationAnalyticsSink sink;
    private readonly Lock synchronization = new();
    private readonly HashSet<string> uploadBatches = new(StringComparer.Ordinal);
    private readonly HashSet<string> uploadFailureOperations = new(StringComparer.Ordinal);
    private readonly HashSet<long> estimateRevisions = [];
    private readonly HashSet<long> reviewRevisions = [];
    private readonly HashSet<string> stageResults = new(StringComparer.Ordinal);

    /// <summary>Creates a tracker that sends typed observations through the supplied analytics sink.</summary>
    /// <param name="sink">The destination for typed quotation observations.</param>
    public InstantQuotationAnalyticsTracker(IInstantQuotationAnalyticsSink sink)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
