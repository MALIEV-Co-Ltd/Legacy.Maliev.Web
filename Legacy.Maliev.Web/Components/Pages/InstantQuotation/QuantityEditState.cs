namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal sealed class QuantityEditState
{
    private readonly Dictionary<Guid, long> revisions = [];
    private readonly HashSet<Guid> pending = [];

    public bool IsPending(Guid partId) => pending.Contains(partId);

    public void Begin(Guid partId)
    {
        revisions[partId] = revisions.GetValueOrDefault(partId) + 1;
        pending.Add(partId);
    }

    public async Task RepriceAsync(Guid partId, Func<Task> reprice)
    {
        Begin(partId);
        var revision = revisions.GetValueOrDefault(partId);
        await reprice();
        if (revisions.GetValueOrDefault(partId) == revision)
        {
            pending.Remove(partId);
        }
    }

    public void Forget(Guid partId)
    {
        revisions.Remove(partId);
        pending.Remove(partId);
    }
}
