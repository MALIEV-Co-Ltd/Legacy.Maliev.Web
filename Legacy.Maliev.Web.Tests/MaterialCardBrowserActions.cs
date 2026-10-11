using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

internal static class MaterialCardBrowserActions
{
    public static async Task SelectAsync(IPage page, string key)
    {
        var selector = page.Locator("[data-workflow-material-picker] [data-material-selector]");
        var card = selector.Locator($"[role='radio'][data-material-key='{key}']");
        if (await card.CountAsync() == 0)
        {
            await selector.Locator("[data-material-toggle]").ClickAsync();
        }
        try
        {
            await card.ClickAsync();
        }
        catch
        {
            await CaptureClickFailureAsync(card);
            throw;
        }
        await Assertions.Expect(card).ToHaveAttributeAsync("aria-checked", "true");
    }

    private static async Task CaptureClickFailureAsync(ILocator card)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var capture = card.EvaluateAsync<System.Text.Json.JsonElement>("""
                card => {
                    const workflow = card.closest('.instant-quote__workflow');
                    const configuration = card.closest('[data-workflow-configuration]');
                    const content = card.closest('[data-workflow-configuration-content]');
                    const rail = card.closest('[data-material-card-scroll]');
                    const parts = workflow?.querySelector('[data-workflow-parts]');
                    const footer = configuration?.querySelector('.instant-quote__configuration-footer');
                    const rect = element => {
                        if (!element) return null;
                        const bounds = element.getBoundingClientRect();
                        return { top: bounds.top, bottom: bounds.bottom, left: bounds.left, right: bounds.right,
                            height: bounds.height, clientHeight: element.clientHeight,
                            scrollHeight: element.scrollHeight, scrollTop: element.scrollTop };
                    };
                    const bounds = card.getBoundingClientRect();
                    const hit = card.ownerDocument.elementFromPoint(bounds.left + bounds.width / 2, bounds.top + bounds.height / 2);
                    const selected = rail?.querySelector('[aria-checked="true"]');
                    const owner = card.contains(hit) ? 'card' : parts?.contains(hit) ? 'parts'
                        : footer?.contains(hit) ? 'footer' : content?.contains(hit) ? 'content'
                        : configuration?.contains(hit) ? 'configuration' : 'other';
                    return { viewport: { width: innerWidth, height: innerHeight }, card: rect(card), rail: rect(rail),
                        content: rect(content), configuration: rect(configuration), parts: rect(parts), footer: rect(footer),
                        selectedRequested: selected === card, selectedPla: selected?.dataset.materialKey === 'PLA', centerOwner: owner };
                }
                """);
            // Wait cancellation does not cancel Playwright; the caller's page disposal owns final teardown.
            ObserveLateFailure(capture);
            var snapshot = await capture.WaitAsync(timeout.Token);
            var directory = Path.GetFullPath(Path.Combine("TestResults", "material-card-click-diagnostics"));
            Directory.CreateDirectory(directory);
            var write = File.WriteAllTextAsync(Path.Combine(directory, $"click-{Guid.NewGuid():N}.json"), snapshot.GetRawText(), timeout.Token);
            ObserveLateFailure(write);
            await write.WaitAsync(timeout.Token);
        }
        catch (Exception)
        {
            // Best-effort categorical evidence must never replace the original click failure.
        }
    }

    private static void ObserveLateFailure(Task task) =>
        _ = task.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
