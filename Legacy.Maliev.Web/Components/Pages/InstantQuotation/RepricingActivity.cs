namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal sealed class RepricingActivity
{
    private int active;

    public bool IsActive => active > 0;

    public async Task RunAsync(Func<Task> update, Func<Task> notify, Func<Task>? afterUpdate = null)
    {
        active++;
        try
        {
            await notify();
            await update();
            if (afterUpdate is not null) await afterUpdate();
        }
        finally
        {
            active--;
            await notify();
        }
    }
}
