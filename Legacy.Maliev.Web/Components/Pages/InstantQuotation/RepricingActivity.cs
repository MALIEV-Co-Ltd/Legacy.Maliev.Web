namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal sealed class RepricingActivity
{
    private readonly Dictionary<Guid, Operation> owned = [];
    private int active;

    public Func<bool> CaptureOwnership(Guid partId)
    {
        var operation = owned.GetValueOrDefault(partId);
        return () => operation is not null && owned.GetValueOrDefault(partId) == operation;
    }

    public void Forget(Guid partId) => owned.Remove(partId);

    public void ClearOwnership() => owned.Clear();

    public bool IsActive => active > 0;

    public Task RunAsync(Func<Task> update, Func<Task> notify, Func<Task>? afterUpdate = null) =>
        RunCoreAsync(null, update, notify, afterUpdate is null ? null : _ => afterUpdate());

    public Task RunAsync(Guid partId, Func<Task> update, Func<Task> notify,
        Func<Func<bool>, Task>? afterUpdate = null) =>
        RunCoreAsync(partId, update, notify, afterUpdate);

    private async Task RunCoreAsync(Guid? partId, Func<Task> update, Func<Task> notify,
        Func<Func<bool>, Task>? afterUpdate)
    {
        var operation = new Operation();
        active++;
        if (partId is { } id)
        {
            if (owned.Remove(id, out var previous)) Complete(previous);
            owned[id] = operation;
        }
        bool IsCurrent() => !operation.Completed
            && (partId is not { } key || owned.GetValueOrDefault(key) == operation);
        try
        {
            await notify();
            if (!IsCurrent()) return;
            try { await update(); }
            catch (InstantQuotationWorkflowCoordinator.ObsoleteQuoteCancellationException) when (!IsCurrent())
            {
                // The old callback fault is observed by its producer; it must not fail a current UI edit.
            }
            if (IsCurrent() && afterUpdate is not null) await afterUpdate(IsCurrent);
        }
        finally
        {
            Complete(operation);
            await notify();
        }
    }

    private void Complete(Operation operation)
    {
        if (operation.Completed) return;
        operation.Completed = true;
        active--;
    }

    private sealed class Operation
    {
        internal bool Completed { get; set; }
    }
}
