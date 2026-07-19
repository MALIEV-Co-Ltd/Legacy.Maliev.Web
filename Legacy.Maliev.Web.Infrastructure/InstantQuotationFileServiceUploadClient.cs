using System.Buffers;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class InstantQuotationFileServiceUploadClient : IInstantQuotationUploadClient
{
    private const int SpoolBufferBytes = 64 * 1024;
    private const int CreationLockCount = 64;
    private const int MaximumConcurrentSpools = 2;
    private static readonly SemaphoreSlim[] CreationLocks = Enumerable
        .Range(0, CreationLockCount)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();
    private static readonly SemaphoreSlim SpoolSlots = new(
        MaximumConcurrentSpools,
        MaximumConcurrentSpools);

    private readonly InstantQuotationFileServiceTransport transport;
    private readonly IInstantQuotationFileCapabilityStore capabilityStore;
    private readonly Func<string> temporaryPathFactory;

    public InstantQuotationFileServiceUploadClient(
        InstantQuotationFileServiceTransport transport,
        IInstantQuotationFileCapabilityStore capabilityStore)
        : this(transport, capabilityStore, CreateTemporaryPath)
    {
    }

    internal InstantQuotationFileServiceUploadClient(
        InstantQuotationFileServiceTransport transport,
        IInstantQuotationFileCapabilityStore capabilityStore,
        Func<string> temporaryPathFactory)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.capabilityStore = capabilityStore ?? throw new ArgumentNullException(nameof(capabilityStore));
        this.temporaryPathFactory = temporaryPathFactory ?? throw new ArgumentNullException(nameof(temporaryPathFactory));
    }

    public async Task<InstantQuotationUploadResult> UploadAsync(
        string sessionId,
        string? ownerIdentity,
        Stream content,
        string fileName,
        string contentType,
        long contentLength,
        InstantQuotationGeometryClaim geometryClaim,
        string operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidSessionId(sessionId)
            || !IsValidOwner(ownerIdentity)
            || content is null
            || !content.CanRead
            || string.IsNullOrWhiteSpace(fileName)
            || string.IsNullOrWhiteSpace(contentType)
            || contentLength <= 0
            || geometryClaim is null
            || !geometryClaim.IsValid()
            || !IsValidOperationId(operationId))
        {
            return ValidationUpload(operationId);
        }

        InstantQuotationFileCapability? capability;
        try
        {
            capability = await GetOrCreateCapabilityAsync(sessionId, ownerIdentity, cancellationToken);
        }
        catch (Exception exception) when (IsNonCancellationFailure(exception, cancellationToken))
        {
            return DependencyUpload(operationId);
        }

        if (capability is null || contentLength > capability.MaxUploadBytes)
        {
            return capability is null ? DependencyUpload(operationId) : ValidationUpload(operationId);
        }

        string? temporaryPath = null;
        var spoolAcquired = false;
        try
        {
            await SpoolSlots.WaitAsync(cancellationToken);
            spoolAcquired = true;
            temporaryPath = temporaryPathFactory();
            if (string.IsNullOrWhiteSpace(temporaryPath)
                || !await SpoolExactAsync(content, temporaryPath, contentLength, cancellationToken))
            {
                return ValidationUpload(operationId);
            }

            var upload = new InstantQuotationFileServiceUpload(
                fileName,
                contentType,
                contentLength,
                token => OpenSpoolAsync(temporaryPath, token));
            var result = await transport.UploadAsync(capability, upload, operationId, cancellationToken);
            if (result.Status != InstantQuotationOperationStatus.Succeeded)
            {
                return InstantQuotationUploadResult.Failed(
                    operationId,
                    result.ServiceStatus,
                    result.AuthorizationStatus,
                    result.ProblemCategory,
                    MapRetry(result.RetryDisposition));
            }

            if (result.File is null || result.File.FileId == Guid.Empty)
            {
                return UnexpectedUpload(operationId);
            }

            if (!string.Equals(result.File.Sha256, geometryClaim.Sha256, StringComparison.Ordinal))
            {
                await transport.RemoveAsync(capability, result.File.FileId, cancellationToken);
                return ValidationUpload(operationId);
            }

            return InstantQuotationUploadResult.Succeeded(
                operationId,
                new InstantQuotationUploadReference(result.File.FileId.ToString("D")),
                result.File.Sha256);
        }
        catch (Exception exception) when (IsNonCancellationFailure(exception, cancellationToken))
        {
            return DependencyUpload(operationId);
        }
        finally
        {
            TryDelete(temporaryPath);
            if (spoolAcquired)
            {
                SpoolSlots.Release();
            }
        }
    }

    public async Task<InstantQuotationRemoveResult> RemoveAsync(
        string sessionId,
        string? ownerIdentity,
        InstantQuotationUploadReference uploadReference,
        string operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidSessionId(sessionId)
            || !IsValidOwner(ownerIdentity)
            || !TryParseReference(uploadReference, out var fileId)
            || !IsValidOperationId(operationId))
        {
            return ValidationRemove(operationId);
        }

        try
        {
            var capability = await capabilityStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
            if (capability is null)
            {
                return DependencyRemove(operationId);
            }

            var result = await transport.RemoveAsync(capability, fileId, cancellationToken);
            return result.Status == InstantQuotationOperationStatus.Succeeded
                ? new InstantQuotationRemoveResult(
                    operationId,
                    result.ServiceStatus,
                    result.AuthorizationStatus,
                    result.Status,
                    result.ProblemCategory)
                : InstantQuotationRemoveResult.Failed(
                    operationId,
                    result.ServiceStatus,
                    result.AuthorizationStatus,
                    result.ProblemCategory);
        }
        catch (Exception exception) when (IsNonCancellationFailure(exception, cancellationToken))
        {
            return DependencyRemove(operationId);
        }
    }

    public async Task<InstantQuotationFinalizationResult> FinalizeAsync(
        string sessionId,
        string? ownerIdentity,
        int quotationRequestId,
        IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
        string operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidSessionId(sessionId)
            || !IsValidOwner(ownerIdentity)
            || quotationRequestId <= 0
            || uploadReferences is null
            || uploadReferences.Count is <= 0 or > 100
            || !TryParseReferences(uploadReferences, out var fileIds)
            || !IsValidOperationId(operationId))
        {
            return ValidationFinalization(operationId);
        }

        try
        {
            var capability = await capabilityStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
            if (capability is null)
            {
                return DependencyFinalization(operationId);
            }

            var result = await transport.FinalizeAsync(
                capability,
                quotationRequestId,
                fileIds,
                operationId,
                cancellationToken);
            if (result.Status != InstantQuotationOperationStatus.Succeeded)
            {
                return InstantQuotationFinalizationResult.Failed(
                    operationId,
                    result.ServiceStatus,
                    result.AuthorizationStatus,
                    result.ProblemCategory);
            }

            if (result.QuotationRequestId != quotationRequestId
                || result.Files.Count != fileIds.Count
                || !result.Files.Select(file => file.FileId).Order().SequenceEqual(fileIds.Order())
                || result.Files.Any(file => file.Bucket.Length is <= 0 or > 50))
            {
                return UnexpectedFinalization(operationId);
            }

            var finalizedFiles = result.Files
                .OrderBy(file => file.FileId)
                .Select(file => new InstantQuotationFinalizedFile(
                    file.FileId,
                    file.Bucket,
                    file.ObjectName,
                    file.FileName,
                    file.ContentType,
                    file.SizeBytes,
                    file.Sha256))
                .ToArray();
            return InstantQuotationFinalizationResult.Succeeded(operationId, finalizedFiles);
        }
        catch (Exception exception) when (IsNonCancellationFailure(exception, cancellationToken))
        {
            return DependencyFinalization(operationId);
        }
    }

    private async Task<InstantQuotationFileCapability?> GetOrCreateCapabilityAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        var existing = await capabilityStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var creationLock = CreationLock(sessionId, ownerIdentity);
        await creationLock.WaitAsync(cancellationToken);
        try
        {
            existing = await capabilityStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var created = await transport.CreateSessionAsync(cancellationToken);
            if (created.Status != InstantQuotationOperationStatus.Succeeded
                || created.Capability is null
                || !await capabilityStore.PutAsync(
                    sessionId,
                    ownerIdentity,
                    created.Capability,
                    cancellationToken))
            {
                return null;
            }

            return created.Capability;
        }
        finally
        {
            creationLock.Release();
        }
    }

    private static async Task<bool> SpoolExactAsync(
        Stream source,
        string path,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(SpoolBufferBytes);
        try
        {
            var options = new FileStreamOptions
            {
                Access = FileAccess.Write,
                BufferSize = SpoolBufferBytes,
                Mode = FileMode.CreateNew,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using var destination = new FileStream(path, options);
            var remaining = expectedLength;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken);
                if (read == 0)
                {
                    return false;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
            }

            if (await source.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
            {
                return false;
            }

            await destination.FlushAsync(cancellationToken);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static ValueTask<Stream> OpenSpoolAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Stream>(new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            SpoolBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    private static string CreateTemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"legacy-web-instant-quotation-{Guid.NewGuid():N}.tmp");

    private static bool TryParseReferences(
        IReadOnlyList<InstantQuotationUploadReference> references,
        out IReadOnlyList<Guid> fileIds)
    {
        var parsed = new Guid[references.Count];
        for (var index = 0; index < references.Count; index++)
        {
            if (!TryParseReference(references[index], out parsed[index]))
            {
                fileIds = [];
                return false;
            }
        }

        if (parsed.Distinct().Count() != parsed.Length)
        {
            fileIds = [];
            return false;
        }

        fileIds = parsed;
        return true;
    }

    private static bool TryParseReference(InstantQuotationUploadReference? reference, out Guid fileId)
    {
        fileId = default;
        return reference?.Value is { } value
            && Guid.TryParseExact(value, "D", out fileId)
            && string.Equals(value, fileId.ToString("D"), StringComparison.Ordinal);
    }

    private static SemaphoreSlim CreationLock(string sessionId, string? ownerIdentity)
    {
        var hash = HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(sessionId),
            ownerIdentity is null ? 0 : StringComparer.Ordinal.GetHashCode(ownerIdentity));
        return CreationLocks[(int)((uint)hash % CreationLockCount)];
    }

    private static bool IsValidSessionId(string? value) =>
        value is { Length: 64 }
        && value.All(Uri.IsHexDigit);

    private static bool IsValidOwner(string? value) =>
        value is null || value is { Length: <= 512 } && !string.IsNullOrWhiteSpace(value);

    private static bool IsValidOperationId(string? value) =>
        value is { Length: >= 16 and <= 128 }
        && value.All(static character => character is >= '!' and <= '~');

    private static bool IsNonCancellationFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private static InstantQuotationUploadRetryDisposition MapRetry(
        InstantQuotationFileServiceRetryDisposition disposition) => disposition switch
        {
            InstantQuotationFileServiceRetryDisposition.RetryIdentical =>
                InstantQuotationUploadRetryDisposition.RetryIdentical,
            InstantQuotationFileServiceRetryDisposition.RetryWithBackoff =>
                InstantQuotationUploadRetryDisposition.RetryWithBackoff,
            _ => InstantQuotationUploadRetryDisposition.None,
        };

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static InstantQuotationUploadResult ValidationUpload(string operationId) =>
        InstantQuotationUploadResult.Failed(
            operationId,
            InstantQuotationServiceStatus.Available,
            InstantQuotationAuthorizationStatus.NotEvaluated,
            InstantQuotationProblemCategory.Validation);

    private static InstantQuotationUploadResult UnexpectedUpload(string operationId) =>
        InstantQuotationUploadResult.Failed(
            operationId,
            InstantQuotationServiceStatus.Available,
            InstantQuotationAuthorizationStatus.Authorized,
            InstantQuotationProblemCategory.Unexpected);

    private static InstantQuotationUploadResult DependencyUpload(string operationId) =>
        InstantQuotationUploadResult.Failed(
            operationId,
            InstantQuotationServiceStatus.Unavailable,
            InstantQuotationAuthorizationStatus.NotEvaluated,
            InstantQuotationProblemCategory.DependencyUnavailable);

    private static InstantQuotationRemoveResult ValidationRemove(string operationId) =>
        InstantQuotationRemoveResult.Failed(
            operationId,
            InstantQuotationServiceStatus.Available,
            InstantQuotationAuthorizationStatus.NotEvaluated,
            InstantQuotationProblemCategory.Validation);

    private static InstantQuotationRemoveResult DependencyRemove(string operationId) =>
        InstantQuotationRemoveResult.Failed(
            operationId,
            InstantQuotationServiceStatus.Unavailable,
            InstantQuotationAuthorizationStatus.NotEvaluated,
            InstantQuotationProblemCategory.DependencyUnavailable);

    private static InstantQuotationFinalizationResult ValidationFinalization(string operationId) =>
        InstantQuotationFinalizationResult.Failed(
            operationId,
            InstantQuotationServiceStatus.Available,
            InstantQuotationAuthorizationStatus.NotEvaluated,
            InstantQuotationProblemCategory.Validation);

    private static InstantQuotationFinalizationResult UnexpectedFinalization(string operationId) =>
        InstantQuotationFinalizationResult.Failed(
            operationId,
            InstantQuotationServiceStatus.Available,
            InstantQuotationAuthorizationStatus.Authorized,
            InstantQuotationProblemCategory.Unexpected);

    private static InstantQuotationFinalizationResult DependencyFinalization(string operationId) =>
        InstantQuotationFinalizationResult.Failed(
            operationId,
            InstantQuotationServiceStatus.Unavailable,
            InstantQuotationAuthorizationStatus.NotEvaluated,
            InstantQuotationProblemCategory.DependencyUnavailable);
}
