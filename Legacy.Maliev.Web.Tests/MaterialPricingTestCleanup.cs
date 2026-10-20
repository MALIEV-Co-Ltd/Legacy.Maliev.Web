using System.Runtime.ExceptionServices;

namespace Legacy.Maliev.Web.Tests;

internal sealed record MaterialPricingFixtureCleanupReceipt(string ScopeState, string FactoryState, bool PrimaryFailureRetained);
internal sealed record MaterialPricingQuoteCleanupReceipt(bool ActualTaskSettled, string TaskState,
    bool CallerCancellationRequested, bool TimedOut, string? CleanupFailureType,
    string? WaitFailureType = null, string? ActualFailureType = null, bool ExpectedTerminalFailure = false,
    string TaskKind = "quote");

internal static class MaterialPricingTestCleanup
{
    public static async ValueTask ReleaseAllAsync(Exception? primary, params Func<ValueTask>[] releases)
    {
        var failures = new List<Exception>();
        foreach (var release in releases)
        {
            try { await release(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count == 0) return;
        var retained = primary ?? failures[0];
        retained.Data["MaterialPricingCleanupFailures"] = failures.ToArray();
        if (primary is null) ExceptionDispatchInfo.Capture(retained).Throw();
    }

    public static void ReleaseAll(params Action[] releases)
    {
        Exception? failure = null;
        foreach (var release in releases)
        {
            try { release(); }
            catch (Exception exception) { failure ??= exception; }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public static async Task RunAsync(Func<Task> body, Func<ValueTask> disposeScope,
        Func<ValueTask> disposeFactory, Action<MaterialPricingFixtureCleanupReceipt> record)
    {
        Exception? primary = null;
        try { await body(); }
        catch (Exception exception) { primary = exception; throw; }
        finally { await DisposeAsync(disposeScope, disposeFactory, primary, record); }
    }

    public static async Task DisposeAsync(Func<ValueTask> disposeScope, Func<ValueTask> disposeFactory,
        Exception? primary, Action<MaterialPricingFixtureCleanupReceipt> record)
    {
        Exception? failure = null;
        string scope = "disposed", factory = "disposed";
        try { await disposeScope(); }
        catch (Exception exception) { failure = exception; scope = "faulted"; }
        try { await disposeFactory(); }
        catch (Exception exception) { failure ??= exception; factory = "faulted"; }
        try { record(new(scope, factory, primary is not null)); }
        catch (Exception exception) { failure ??= exception; }
        if (primary is null && failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public static async Task DrainQuoteAsync(Task owned, Action cancel, Action release,
        CancellationToken caller, Exception? primary, Action<MaterialPricingQuoteCleanupReceipt> record, TimeSpan wait,
        bool expectedTerminalFailure = false, Action? afterWaitFailure = null, string taskKind = "quote")
    {
        Exception? failure = null;
        bool timedOut = false;
        Exception? waitFailure = null;
        try { cancel(); }
        catch (Exception exception) { failure = exception; }
        try { release(); }
        catch (Exception exception) { failure ??= exception; }
        try { await owned.WaitAsync(wait); }
        catch (Exception exception)
        {
            waitFailure = exception;
            timedOut = exception is TimeoutException
                && !ReferenceEquals(owned.Exception?.GetBaseException(), exception);
            if (!(exception is OperationCanceledException && owned.IsCanceled && caller.IsCancellationRequested)
                && !(expectedTerminalFailure && !timedOut && (owned.IsFaulted || owned.IsCanceled))) failure ??= exception;
            // Deterministic test-only race seam; integration call sites leave this null.
            try { afterWaitFailure?.Invoke(); }
            catch (Exception boundaryFailure) { failure ??= boundaryFailure; }
        }
        TaskStatus status = owned.Status;
        bool settled = status is TaskStatus.RanToCompletion or TaskStatus.Faulted or TaskStatus.Canceled;
        Exception? actualFailure = status == TaskStatus.Faulted ? owned.Exception?.GetBaseException() : null;
        if (actualFailure is not null && !expectedTerminalFailure) failure ??= actualFailure;
        var receipt = new MaterialPricingQuoteCleanupReceipt(settled, status.ToString(),
            caller.IsCancellationRequested, timedOut, failure?.GetType().Name, waitFailure?.GetType().Name,
            actualFailure?.GetType().Name ?? (status == TaskStatus.Canceled ? nameof(OperationCanceledException) : null),
            expectedTerminalFailure, taskKind);
        try { record(receipt); }
        catch (Exception exception) { failure ??= exception; }
        if (primary is null && failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
