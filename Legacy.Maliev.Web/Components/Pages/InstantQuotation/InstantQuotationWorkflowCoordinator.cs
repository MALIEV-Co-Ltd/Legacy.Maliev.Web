using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public sealed record InstantQuotationWorkflowUploadFile(
    string FileName,
    string ContentType,
    long Length,
    InstantQuotationGeometryClaim GeometryClaim,
    Func<CancellationToken, Task<Stream>> OpenReadStreamAsync);

public enum InstantQuotationWorkflowUploadStatus
{
    Pending,
    Uploading,
    Uploaded,
    Error,
    Cancelled,
}

public sealed record InstantQuotationWorkflowUploadViewModel(
    Guid LocalId,
    string DisplayFileName,
    InstantQuotationWorkflowUploadStatus Status,
    InstantQuotationProblemCategory ProblemCategory);

public sealed record InstantQuotationWorkflowMaterialOption(string Key, string DisplayName);

public sealed record InstantQuotationWorkflowPartViewModel(
    Guid PartId,
    Guid PreviewCorrelationId,
    string DisplayFileName,
    AuthoritativeInstantQuotationGeometry Geometry,
    InstantQuotationPartConfiguration Configuration,
    InstantQuotationPartQuote? Quote);

public interface IInstantQuotationWorkflowSessionIdentityAccessor
{
    ValueTask<string?> GetProtectedSessionIdentityAsync(CancellationToken cancellationToken);

    ValueTask SetProtectedSessionIdentityAsync(
        string protectedSessionIdentity,
        CancellationToken cancellationToken);
}

public sealed class InstantQuotationWorkflowCoordinator : IAsyncDisposable
{
    public const long MaximumFileSize = 200 * 1024 * 1024;

    private static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(
        [".stl", ".obj", ".3mf", ".glb", ".gltf", ".stp", ".step", ".igs", ".iges"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IInstantQuotationSessionStore sessionStore;
    private readonly IInstantQuotationUploadClient uploadClient;
    private readonly IInstantQuotationPricingService pricingService;
    private readonly IInstantQuotationAnalyticsTracker analytics;
    private readonly string? ownerIdentity;
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly List<UploadEntry> entries = [];
    private InstantQuotationSessionState? session;
    private long authoritativeQuoteRevision;
    private bool disposed;

    public InstantQuotationWorkflowCoordinator(
        IInstantQuotationSessionStore sessionStore,
        IInstantQuotationUploadClient uploadClient,
        IInstantQuotationPricingService pricingService,
        string? ownerIdentity,
        IInstantQuotationAnalyticsTracker? analytics = null)
    {
        this.sessionStore = sessionStore;
        this.uploadClient = uploadClient;
        this.pricingService = pricingService;
        this.ownerIdentity = ownerIdentity;
        this.analytics = analytics ?? NoOpInstantQuotationAnalyticsTracker.Instance;
    }

    public InstantQuotationWorkflowState State { get; private set; } = InstantQuotationWorkflowState.Empty;

    public string ProtectedSessionIdentity
    {
        get
        {
            EnsureInitialized();
            return session!.SessionId;
        }
    }

    public IReadOnlyList<InstantQuotationWorkflowUploadViewModel> Uploads => entries
        .Select(static entry => new InstantQuotationWorkflowUploadViewModel(
            entry.LocalId,
            entry.File.FileName,
            entry.Status,
            entry.ProblemCategory))
        .ToArray();

    public IReadOnlyList<InstantQuotationWorkflowPartViewModel> Parts
    {
        get
        {
            var quotes = OrderQuote?.Parts.ToDictionary(static part => part.PartId) ?? [];
            return entries
                .Where(static entry => entry.Part is not null)
                .Select(entry => new InstantQuotationWorkflowPartViewModel(
                    entry.Part!.PartId,
                    entry.LocalId,
                    entry.Part.DisplayFileName,
                    entry.Part.Geometry,
                    entry.Part.Configuration,
                    quotes.GetValueOrDefault(entry.Part.PartId)))
                .ToArray();
        }
    }

    public InstantQuotationOrderQuote? OrderQuote { get; private set; }

    public long AuthoritativeQuoteRevision => authoritativeQuoteRevision;

    public bool HasCompleteAuthoritativeQuote => IsCompleteAuthoritativeQuote();

    public bool HasCompleteAuthoritativeEstimate => IsCompleteAuthoritativeEstimate();

    public IReadOnlyList<InstantQuotationWorkflowMaterialOption> Materials { get; } = PricingCatalog.Materials.Values
        .Select(static material => new InstantQuotationWorkflowMaterialOption(material.Key, material.DisplayName))
        .ToArray();

    public IReadOnlyList<string> GetColors(string materialKey) =>
        PricingCatalog.MaterialColors.TryGetValue(materialKey, out var colors) ? colors : [];

    public Task InitializeAsync(CancellationToken cancellationToken) => InitializeAsync(null, cancellationToken);

    public async Task InitializeAsync(
        string? protectedSessionIdentity,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (session is not null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(protectedSessionIdentity))
        {
            var existing = await sessionStore.GetAsync(
                protectedSessionIdentity,
                ownerIdentity,
                cancellationToken);
            if (existing is not null && TryRestore(existing))
            {
                return;
            }
        }

        session = await sessionStore.CreateAsync(ownerIdentity, new InstantQuotationOrderState([]), cancellationToken);
        entries.Clear();
        OrderQuote = null;
        RefreshState();
    }

    public async Task UploadAsync(
        IReadOnlyList<InstantQuotationWorkflowUploadFile> files,
        CancellationToken cancellationToken)
    {
        var reserved = ReserveUploads(files);
        await UploadReservedAsync(reserved, cancellationToken);
    }

    public IReadOnlyList<Guid> ReserveUploads(IReadOnlyList<InstantQuotationWorkflowUploadFile> files)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(files);

        var pending = new List<UploadEntry>(files.Count);
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            var entry = new UploadEntry(file);
            entries.Add(entry);
            pending.Add(entry);
        }

        RefreshState();
        return pending.Select(static entry => entry.LocalId).ToArray();
    }

    public async Task UploadReservedAsync(
        IReadOnlyList<Guid> localIds,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(localIds);
        if (localIds.Count != localIds.Distinct().Count())
        {
            throw new ArgumentException("Reserved upload identifiers must be unique.", nameof(localIds));
        }

        var pending = localIds.Select(localId => entries.SingleOrDefault(entry => entry.LocalId == localId)
                ?? throw new ArgumentException("A reserved upload was not found.", nameof(localIds)))
            .ToArray();
        if (pending.Any(static entry => entry.Status is not InstantQuotationWorkflowUploadStatus.Pending))
        {
            throw new InvalidOperationException("Only pending reserved uploads can be started.");
        }

        await Task.WhenAll(pending.Select(entry => StartUploadAsync(entry, cancellationToken)));
    }

    public void Cancel(Guid localId)
    {
        var entry = entries.SingleOrDefault(item => item.LocalId == localId);
        if (entry is null || entry.Status is not InstantQuotationWorkflowUploadStatus.Uploading)
        {
            return;
        }

        entry.Status = InstantQuotationWorkflowUploadStatus.Cancelled;
        entry.RetryDisposition = InstantQuotationUploadRetryDisposition.RetryIdentical;
        entry.OperationCancellation?.Cancel();
        RefreshState();
    }

    public async Task RetryAsync(Guid localId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var entry = entries.SingleOrDefault(item => item.LocalId == localId)
            ?? throw new ArgumentException("The upload was not found.", nameof(localId));
        if (entry.Part is not null && entry.Status is InstantQuotationWorkflowUploadStatus.Error)
        {
            await RemoveAsync(entry.Part.PartId, cancellationToken);
            return;
        }

        if (entry.Part is not null || entry.Status is InstantQuotationWorkflowUploadStatus.Uploading)
        {
            throw new InvalidOperationException("Only failed or cancelled uploads can be retried.");
        }

        entry.ProblemCategory = InstantQuotationProblemCategory.None;
        await StartUploadAsync(entry, cancellationToken);
    }

    public async Task RemoveAsync(Guid partId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        var entry = entries.SingleOrDefault(item => item.Part?.PartId == partId)
            ?? throw new ArgumentException("The part was not found.", nameof(partId));
        var operationId = NewOperationId();
        var result = await uploadClient.RemoveAsync(
            session!.SessionId,
            ownerIdentity,
            entry.Part!.UploadReference,
            operationId,
            cancellationToken);
        if (result.OperationId != operationId
            || result.ServiceStatus is not InstantQuotationServiceStatus.Available
            || result.AuthorizationStatus is not InstantQuotationAuthorizationStatus.Authorized
            || result.Status is not InstantQuotationOperationStatus.Succeeded
            || result.ProblemCategory is not InstantQuotationProblemCategory.None)
        {
            entry.Status = InstantQuotationWorkflowUploadStatus.Error;
            entry.ProblemCategory = result.ProblemCategory;
            RefreshState();
            return;
        }

        await stateGate.WaitAsync(cancellationToken);
        try
        {
            entries.Remove(entry);
            await PersistAndPriceAsync(cancellationToken);
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async Task UpdateConfigurationAsync(
        Guid partId,
        string materialKey,
        string color,
        int quantity,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        var material = PricingCatalog.ResolveMaterial(materialKey)
            ?? throw new ArgumentException("The selected material is not supported.", nameof(materialKey));
        if (!PricingCatalog.IsColorSupported(material.Key, color))
        {
            throw new ArgumentException("The selected color is not supported.", nameof(color));
        }

        if (quantity is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        await stateGate.WaitAsync(cancellationToken);
        try
        {
            var entry = entries.SingleOrDefault(item => item.Part?.PartId == partId)
                ?? throw new ArgumentException("The part was not found.", nameof(partId));
            var previous = entry.Part!;
            entry.Part = previous with
            {
                Configuration = new InstantQuotationPartConfiguration(material.Key, color, quantity),
            };
            entry.HasConfigured = true;
            await PersistAndPriceAsync(cancellationToken);
        }
        finally
        {
            stateGate.Release();
        }
    }

    public void EnterReview()
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (State is not InstantQuotationWorkflowState.Configured || !IsCompleteAuthoritativeQuote())
        {
            throw new InvalidOperationException("A complete authoritative quotation is required before review.");
        }

        State = InstantQuotationWorkflowState.Review;
    }

    public void EnterCustomerDetails()
    {
        ThrowIfDisposed();
        if (State is not InstantQuotationWorkflowState.Review || !IsCompleteAuthoritativeQuote())
        {
            throw new InvalidOperationException("The authoritative quotation must be reviewed before customer details.");
        }

        State = InstantQuotationWorkflowState.CustomerDetails;
    }

    public void ReturnToConfiguration()
    {
        ThrowIfDisposed();
        if (State is InstantQuotationWorkflowState.Review)
        {
            RefreshState();
        }
    }

    public void ReturnToReview()
    {
        ThrowIfDisposed();
        if (State is InstantQuotationWorkflowState.CustomerDetails && IsCompleteAuthoritativeQuote())
        {
            State = InstantQuotationWorkflowState.Review;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCancellation.Cancel();
        foreach (var entry in entries)
        {
            entry.OperationId = null;
            if (entry.Status is InstantQuotationWorkflowUploadStatus.Uploading)
            {
                entry.Status = InstantQuotationWorkflowUploadStatus.Cancelled;
            }

            entry.OperationCancellation?.Cancel();
        }

        RefreshState();
        lifetimeCancellation.Dispose();
        await Task.CompletedTask;
    }

    private async Task StartUploadAsync(UploadEntry entry, CancellationToken cancellationToken)
    {
        entry.OperationCancellation?.Dispose();
        entry.OperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetimeCancellation.Token);
        var operationId = entry.RetryDisposition is InstantQuotationUploadRetryDisposition.RetryIdentical
            && entry.OperationId is { Length: > 0 } retainedOperationId
                ? retainedOperationId
                : NewOperationId();
        entry.OperationId = operationId;
        entry.RetryDisposition = InstantQuotationUploadRetryDisposition.None;
        entry.Status = InstantQuotationWorkflowUploadStatus.Uploading;
        RefreshState();

        if (entry.File.Length is <= 0 or > MaximumFileSize)
        {
            entry.Status = InstantQuotationWorkflowUploadStatus.Error;
            entry.ProblemCategory = InstantQuotationProblemCategory.Validation;
            RefreshState();
            await analytics.RecordUploadFailureAsync(
                operationId,
                InstantQuotationProblemCategory.Validation,
                1);
            return;
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(entry.File.FileName)))
        {
            entry.Status = InstantQuotationWorkflowUploadStatus.Error;
            entry.ProblemCategory = InstantQuotationProblemCategory.Validation;
            RefreshState();
            await analytics.RecordUploadFailureAsync(
                operationId,
                InstantQuotationProblemCategory.Validation,
                1);
            return;
        }

        if (entry.File.GeometryClaim is null || !entry.File.GeometryClaim.IsValid())
        {
            entry.Status = InstantQuotationWorkflowUploadStatus.Error;
            entry.ProblemCategory = InstantQuotationProblemCategory.Validation;
            RefreshState();
            await analytics.RecordUploadFailureAsync(
                operationId,
                InstantQuotationProblemCategory.Validation,
                1);
            return;
        }

        try
        {
            await using var content = await entry.File.OpenReadStreamAsync(entry.OperationCancellation.Token);
            var result = await uploadClient.UploadAsync(
                session!.SessionId,
                ownerIdentity,
                content,
                entry.File.FileName,
                entry.File.ContentType,
                entry.File.Length,
                entry.File.GeometryClaim,
                operationId,
                entry.OperationCancellation.Token);
            await ApplyUploadResultAsync(entry, operationId, result, cancellationToken);
        }
        catch (OperationCanceledException) when (entry.OperationCancellation.IsCancellationRequested)
        {
            if (entry.OperationId == operationId)
            {
                entry.Status = InstantQuotationWorkflowUploadStatus.Cancelled;
                entry.RetryDisposition = InstantQuotationUploadRetryDisposition.RetryIdentical;
                RefreshState();
            }
        }
        catch
        {
            if (entry.OperationId == operationId)
            {
                entry.Status = InstantQuotationWorkflowUploadStatus.Error;
                entry.ProblemCategory = InstantQuotationProblemCategory.Unexpected;
                RefreshState();
                await analytics.RecordUploadFailureAsync(
                    operationId,
                    InstantQuotationProblemCategory.Unexpected,
                    1);
            }
        }
    }

    private async Task ApplyUploadResultAsync(
        UploadEntry entry,
        string operationId,
        InstantQuotationUploadResult result,
        CancellationToken cancellationToken)
    {
        InstantQuotationProblemCategory? terminalFailure = null;
        await stateGate.WaitAsync(cancellationToken);
        try
        {
            var reconcilesRetainedOperation = entry.OperationId == operationId
                && entry.Part is null
                && entry.Status is InstantQuotationWorkflowUploadStatus.Error
                && IsAuthoritativeSuccess(result);
            if ((entry.OperationId != operationId
                    || entry.Status is not InstantQuotationWorkflowUploadStatus.Uploading)
                && !reconcilesRetainedOperation)
            {
                if (IsPersistedSuccess(result))
                {
                    var matchesPromotedPart = entry.Part is not null
                        && string.Equals(
                            entry.Part.UploadReference.Value,
                            result.UploadReference!.Value,
                            StringComparison.Ordinal);
                    if (!matchesPromotedPart)
                    {
                        await RemoveStaleUploadAsync(result.UploadReference!);
                    }

                    entry.RetryDisposition = InstantQuotationUploadRetryDisposition.None;
                }

                return;
            }

            if (result.OperationId != operationId || !IsAuthoritativeSuccess(result))
            {
                if (result.OperationId != operationId && IsPersistedSuccess(result))
                {
                    await RemoveStaleUploadAsync(result.UploadReference!);
                }

                entry.Status = InstantQuotationWorkflowUploadStatus.Error;
                entry.ProblemCategory = result.ProblemCategory is InstantQuotationProblemCategory.None
                    ? InstantQuotationProblemCategory.Unexpected
                    : result.ProblemCategory;
                entry.RetryDisposition = result.OperationId == operationId
                    ? result.RetryDisposition
                    : InstantQuotationUploadRetryDisposition.None;
                terminalFailure = entry.ProblemCategory;
                RefreshState();
            }
            else
            {
                var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    result,
                    entry.File.GeometryClaim);
                if (geometry is null)
                {
                    if (IsPersistedSuccess(result))
                    {
                        await RemoveStaleUploadAsync(result.UploadReference!);
                    }

                    entry.Status = InstantQuotationWorkflowUploadStatus.Error;
                    entry.ProblemCategory = InstantQuotationProblemCategory.Validation;
                    entry.RetryDisposition = InstantQuotationUploadRetryDisposition.None;
                    terminalFailure = entry.ProblemCategory;
                    RefreshState();
                }
                else
                {
                    entry.Part = new InstantQuotationPart(
                        Guid.NewGuid(),
                        entry.File.FileName,
                        result.UploadReference!,
                        geometry,
                        new InstantQuotationPartConfiguration("PLA", "Black", 1));
                    entry.Status = InstantQuotationWorkflowUploadStatus.Uploaded;
                    entry.ProblemCategory = InstantQuotationProblemCategory.None;
                    entry.RetryDisposition = InstantQuotationUploadRetryDisposition.None;
                    await PersistAndPriceAsync(cancellationToken);
                }
            }
        }
        finally
        {
            stateGate.Release();
        }

        if (terminalFailure is { } failure)
        {
            await analytics.RecordUploadFailureAsync(operationId, failure, 1);
        }
    }

    private async Task RemoveStaleUploadAsync(InstantQuotationUploadReference reference)
    {
        using var cleanupCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await uploadClient.RemoveAsync(
                session!.SessionId,
                ownerIdentity,
                reference,
                NewOperationId(),
                cleanupCancellation.Token);
        }
        catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            // The stale result remains quarantined from quote state even when dependency cleanup is unavailable.
        }
    }

    private static bool IsAuthoritativeSuccess(InstantQuotationUploadResult result) =>
        IsPersistedSuccess(result)
        && result.ContentSha256 is { Length: 64 };

    private static bool IsPersistedSuccess(InstantQuotationUploadResult result) =>
        result.ServiceStatus is InstantQuotationServiceStatus.Available
        && result.AuthorizationStatus is InstantQuotationAuthorizationStatus.Authorized
        && result.Status is InstantQuotationOperationStatus.Succeeded
        && result.ProblemCategory is InstantQuotationProblemCategory.None
        && result.UploadReference is not null;

    private bool TryRestore(InstantQuotationSessionState existing)
    {
        try
        {
            var parts = existing.RequestState?.Parts?.ToArray();
            if (parts is null || parts.Any(static part =>
                    part is null
                    || part.PartId == Guid.Empty
                    || string.IsNullOrWhiteSpace(part.DisplayFileName)
                    || string.IsNullOrWhiteSpace(part.UploadReference?.Value)
                    || part.Geometry is null
                    || part.Configuration is null))
            {
                return false;
            }

            var restoredQuote = parts.Length == 0
                ? null
                : pricingService.Quote(new InstantQuotationOrderState(parts));
            entries.Clear();
            entries.AddRange(parts.Select(static part => UploadEntry.Restore(part)));
            session = existing;
            OrderQuote = restoredQuote;
            if (restoredQuote is not null)
            {
                authoritativeQuoteRevision++;
            }
            RefreshState();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task PersistAndPriceAsync(CancellationToken cancellationToken)
    {
        var state = new InstantQuotationOrderState(CurrentParts());
        var updated = session! with
        {
            RequestState = state,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        if (!await sessionStore.PutAsync(updated, ownerIdentity, cancellationToken))
        {
            throw new InvalidOperationException("The protected quotation session could not be updated.");
        }

        session = updated;
        OrderQuote = state.Parts.Count == 0 ? null : pricingService.Quote(state);
        if (OrderQuote is not null)
        {
            authoritativeQuoteRevision++;
        }
        RefreshState();
    }

    private InstantQuotationPart[] CurrentParts() => entries
        .Where(static entry => entry.Part is not null)
        .Select(static entry => entry.Part!)
        .ToArray();

    private void RefreshState()
    {
        if (entries.Any(static entry => entry.Status is InstantQuotationWorkflowUploadStatus.Uploading))
        {
            State = InstantQuotationWorkflowState.Uploading;
            return;
        }

        if (entries.Any(static entry => entry.Status is InstantQuotationWorkflowUploadStatus.Error))
        {
            State = InstantQuotationWorkflowState.Error;
            return;
        }

        var parts = CurrentParts();
        State = parts.Length switch
        {
            0 => InstantQuotationWorkflowState.Empty,
            _ when entries.Where(static entry => entry.Part is not null).All(static entry => entry.HasConfigured) =>
                InstantQuotationWorkflowState.Configured,
            > 1 => InstantQuotationWorkflowState.MultiPart,
            _ => InstantQuotationWorkflowState.Uploaded,
        };
    }

    private bool IsCompleteAuthoritativeQuote()
    {
        return IsCompleteAuthoritativeEstimate()
            && entries.Where(static entry => entry.Part is not null).All(static entry => entry.HasConfigured);
    }

    private bool IsCompleteAuthoritativeEstimate()
    {
        var parts = CurrentParts();
        return parts.Length > 0
            && OrderQuote is not null
            && OrderQuote.Parts.Count == parts.Length
            && OrderQuote.Parts.All(static quote => quote.Quantity > 0 && quote.UnitPrice >= 0 && quote.Subtotal >= 0);
    }

    private void EnsureInitialized()
    {
        if (session is null)
        {
            throw new InvalidOperationException("The quotation workflow has not been initialized.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static string NewOperationId() => Guid.NewGuid().ToString("N");

    private sealed class UploadEntry
    {
        public UploadEntry(InstantQuotationWorkflowUploadFile file)
        {
            File = file with
            {
                GeometryClaim = file.GeometryClaim?.Snapshot()!,
            };
        }

        public Guid LocalId { get; } = Guid.NewGuid();

        public InstantQuotationWorkflowUploadFile File { get; }

        public string? OperationId { get; set; }

        public CancellationTokenSource? OperationCancellation { get; set; }

        public InstantQuotationWorkflowUploadStatus Status { get; set; } = InstantQuotationWorkflowUploadStatus.Pending;

        public InstantQuotationProblemCategory ProblemCategory { get; set; }

        public InstantQuotationUploadRetryDisposition RetryDisposition { get; set; }

        public InstantQuotationPart? Part { get; set; }

        public bool HasConfigured { get; set; }

        public static UploadEntry Restore(InstantQuotationPart part) => new(new InstantQuotationWorkflowUploadFile(
            part.DisplayFileName,
            "application/octet-stream",
            1,
            RestoredClaim(part.Geometry),
            _ => throw new InvalidOperationException("A restored upload cannot be read again.")))
        {
            Part = part,
            Status = InstantQuotationWorkflowUploadStatus.Uploaded,
            HasConfigured = true,
        };

        private static InstantQuotationGeometryClaim RestoredClaim(AuthoritativeInstantQuotationGeometry geometry) => new(
            geometry.ClaimVersion,
            geometry.Sha256,
            geometry.DimensionXmm,
            geometry.DimensionYmm,
            geometry.DimensionZmm,
            geometry.VolumeMm3,
            geometry.SurfaceAreaMm2,
            geometry.AreaProfileMm2,
            geometry.PerimeterProfileMm,
            geometry.FacetCount,
            geometry.BodyCount,
            geometry.TopologyChecked,
            geometry.NonWatertight,
            geometry.NonManifold,
            geometry.MinThicknessMm);
    }
}
