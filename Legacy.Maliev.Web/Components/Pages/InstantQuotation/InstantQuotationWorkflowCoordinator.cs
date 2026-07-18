using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public sealed record InstantQuotationWorkflowUploadFile(
    string FileName,
    string ContentType,
    long Length,
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
    string DisplayFileName,
    InstantQuotationPartConfiguration Configuration,
    InstantQuotationPartQuote? Quote);

public sealed class InstantQuotationWorkflowCoordinator : IAsyncDisposable
{
    public const long MaximumFileSize = 200 * 1024 * 1024;

    private readonly IInstantQuotationSessionStore sessionStore;
    private readonly IInstantQuotationUploadClient uploadClient;
    private readonly IInstantQuotationPricingService pricingService;
    private readonly string? ownerIdentity;
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly List<UploadEntry> entries = [];
    private InstantQuotationSessionState? session;
    private bool disposed;

    public InstantQuotationWorkflowCoordinator(
        IInstantQuotationSessionStore sessionStore,
        IInstantQuotationUploadClient uploadClient,
        IInstantQuotationPricingService pricingService,
        string? ownerIdentity)
    {
        this.sessionStore = sessionStore;
        this.uploadClient = uploadClient;
        this.pricingService = pricingService;
        this.ownerIdentity = ownerIdentity;
    }

    public InstantQuotationWorkflowState State { get; private set; } = InstantQuotationWorkflowState.Empty;

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
            return CurrentParts()
                .Select(part => new InstantQuotationWorkflowPartViewModel(
                    part.PartId,
                    part.DisplayFileName,
                    part.Configuration,
                    quotes.GetValueOrDefault(part.PartId)))
                .ToArray();
        }
    }

    public InstantQuotationOrderQuote? OrderQuote { get; private set; }

    public IReadOnlyList<InstantQuotationWorkflowMaterialOption> Materials { get; } = PricingCatalog.Materials.Values
        .Select(static material => new InstantQuotationWorkflowMaterialOption(material.Key, material.DisplayName))
        .ToArray();

    public IReadOnlyList<string> GetColors(string materialKey) =>
        PricingCatalog.MaterialColors.TryGetValue(materialKey, out var colors) ? colors : [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (session is not null)
        {
            return;
        }

        session = await sessionStore.CreateAsync(
            ownerIdentity,
            new InstantQuotationOrderState([]),
            cancellationToken);
    }

    public async Task UploadAsync(
        IReadOnlyList<InstantQuotationWorkflowUploadFile> files,
        CancellationToken cancellationToken)
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
        entry.OperationCancellation?.Cancel();
        entry.OperationId = null;
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
        var operationId = NewOperationId();
        entry.OperationId = operationId;
        entry.Status = InstantQuotationWorkflowUploadStatus.Uploading;
        RefreshState();

        if (entry.File.Length is <= 0 or > MaximumFileSize)
        {
            entry.Status = InstantQuotationWorkflowUploadStatus.Error;
            entry.ProblemCategory = InstantQuotationProblemCategory.Validation;
            RefreshState();
            return;
        }

        try
        {
            await using var content = await entry.File.OpenReadStreamAsync(entry.OperationCancellation.Token);
            var result = await uploadClient.UploadAsync(
                session!.SessionId,
                content,
                entry.File.FileName,
                entry.File.ContentType,
                entry.File.Length,
                operationId,
                entry.OperationCancellation.Token);
            await ApplyUploadResultAsync(entry, operationId, result, cancellationToken);
        }
        catch (OperationCanceledException) when (entry.OperationCancellation.IsCancellationRequested)
        {
            if (entry.OperationId == operationId)
            {
                entry.Status = InstantQuotationWorkflowUploadStatus.Cancelled;
                entry.OperationId = null;
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
            }
        }
    }

    private async Task ApplyUploadResultAsync(
        UploadEntry entry,
        string operationId,
        InstantQuotationUploadResult result,
        CancellationToken cancellationToken)
    {
        await stateGate.WaitAsync(cancellationToken);
        try
        {
            if (entry.OperationId != operationId
                || entry.Status is not InstantQuotationWorkflowUploadStatus.Uploading)
            {
                if (IsAuthoritativeSuccess(result))
                {
                    await RemoveStaleUploadAsync(result.UploadReference!);
                }

                return;
            }

            if (result.OperationId != operationId || !IsAuthoritativeSuccess(result))
            {
                entry.Status = InstantQuotationWorkflowUploadStatus.Error;
                entry.ProblemCategory = result.ProblemCategory is InstantQuotationProblemCategory.None
                    ? InstantQuotationProblemCategory.Unexpected
                    : result.ProblemCategory;
                RefreshState();
                return;
            }

            entry.Part = new InstantQuotationPart(
                Guid.NewGuid(),
                entry.File.FileName,
                result.UploadReference!,
                result.AuthoritativeGeometry!,
                new InstantQuotationPartConfiguration("PLA", "Black", 1));
            entry.Status = InstantQuotationWorkflowUploadStatus.Uploaded;
            entry.ProblemCategory = InstantQuotationProblemCategory.None;
            await PersistAndPriceAsync(cancellationToken);
        }
        finally
        {
            stateGate.Release();
        }
    }

    private async Task RemoveStaleUploadAsync(InstantQuotationUploadReference reference)
    {
        try
        {
            await uploadClient.RemoveAsync(
                session!.SessionId,
                reference,
                NewOperationId(),
                lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            // The stale result remains quarantined from quote state even when dependency cleanup is unavailable.
        }
    }

    private static bool IsAuthoritativeSuccess(InstantQuotationUploadResult result) =>
        result.ServiceStatus is InstantQuotationServiceStatus.Available
        && result.AuthorizationStatus is InstantQuotationAuthorizationStatus.Authorized
        && result.Status is InstantQuotationOperationStatus.Succeeded
        && result.ProblemCategory is InstantQuotationProblemCategory.None
        && result.UploadReference is not null
        && result.AuthoritativeGeometry is not null;

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
            > 1 => InstantQuotationWorkflowState.MultiPart,
            _ when entries.Any(static entry => entry.HasConfigured) => InstantQuotationWorkflowState.Configured,
            _ => InstantQuotationWorkflowState.Uploaded,
        };
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

    private sealed class UploadEntry(InstantQuotationWorkflowUploadFile file)
    {
        public Guid LocalId { get; } = Guid.NewGuid();

        public InstantQuotationWorkflowUploadFile File { get; } = file;

        public string? OperationId { get; set; }

        public CancellationTokenSource? OperationCancellation { get; set; }

        public InstantQuotationWorkflowUploadStatus Status { get; set; } = InstantQuotationWorkflowUploadStatus.Pending;

        public InstantQuotationProblemCategory ProblemCategory { get; set; }

        public InstantQuotationPart? Part { get; set; }

        public bool HasConfigured { get; set; }
    }
}
