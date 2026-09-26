namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal sealed class RepricingActivity
{
    private int active;

    public bool IsActive => active > 0;

    public async Task RunAsync(Func<Task> update, Func<Task> notify)
    {
        active++;
        try
        {
            await notify();
            await update();
        }
        finally
        {
            active--;
            await notify();
        }
    }
}
