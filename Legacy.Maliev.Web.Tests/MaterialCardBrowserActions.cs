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
        await card.ClickAsync();
        await Assertions.Expect(card).ToHaveAttributeAsync("aria-checked", "true");
    }
}
