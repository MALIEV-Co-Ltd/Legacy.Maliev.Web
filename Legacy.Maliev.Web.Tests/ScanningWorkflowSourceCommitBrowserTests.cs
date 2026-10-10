using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class ScanningWorkflowSourceCommitBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", 1440, true, false)]
    [InlineData("th", 1440, true, false)]
    [InlineData("en", 960, true, false)]
    [InlineData("th", 960, true, false)]
    [InlineData("en", 320, true, false)]
    [InlineData("th", 320, true, false)]
    [InlineData("en", 1440, true, true)]
    [InlineData("th", 320, true, true)]
    [InlineData("en", 1440, false, false)]
    [InlineData("th", 320, false, false)]
    public async Task OriginalWorkflow_RemainsReadableWithResponsiveMotionAndNoScriptFallback(
        string culture, int width, bool javaScript, bool reducedMotion)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
            JavaScriptEnabled = javaScript,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        await using var page = await context.NewPageAsync();
        var url = new Uri(new Uri(fixture.CncQuotationUrl), $"/services/3d-scanning?culture={culture}").ToString();
        await page.GotoAsync(url);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (javaScript && await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        var workflow = page.Locator("#scanning-selection-guide");
        var stages = workflow.Locator(".scanning-workflow-step");
        Assert.Equal(5, await stages.CountAsync());
        Assert.Equal(5, await workflow.Locator(".scanning-workflow-image").CountAsync());
        var imageSources = new[]
        {
            "/src/images/services/scanning/workflow/scanning-workflow-capture.webp",
            "/src/images/services/scanning/art/scanning-art-raw-capture.webp",
            "/src/images/services/scanning/workflow/scanning-workflow-clean-mesh.webp",
            "/src/images/services/scanning/workflow/scanning-workflow-reverse-engineering.webp",
            "/src/images/services/scanning/workflow/scanning-workflow-deviation-analysis.webp",
        };
        for (var index = 0; index < 5; index++)
        {
            var stage = stages.Nth(index);
            await stage.ScrollIntoViewIfNeededAsync();
            await page.WaitForFunctionAsync("index => Number(getComputedStyle(document.querySelectorAll('#scanning-selection-guide .scanning-workflow-step')[index]).opacity) === 1", index);
            if (javaScript)
            {
                Assert.True(await page.Locator("html").EvaluateAsync<bool>("html => html.classList.contains('js')"));
                Assert.True(await workflow.Locator("[data-scanning-workflow]").EvaluateAsync<bool>("timeline => timeline.classList.contains('is-active') && timeline.dataset.scanningWorkflowRevealed === 'true'"));
                Assert.True(await stage.EvaluateAsync<bool>("stage => stage.classList.contains('is-visible')"));
            }
            Assert.True(await stage.Locator("h3").IsVisibleAsync());
            Assert.Equal((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), await stage.Locator(".scanning-workflow-node").InnerTextAsync());
            Assert.True(await stage.Locator(".scanning-workflow-output").IsVisibleAsync());
            var image = stage.Locator(".scanning-workflow-image");
            Assert.Equal(imageSources[index], await image.GetAttributeAsync("src"));
            Assert.Equal("lazy", await image.GetAttributeAsync("loading"));
            Assert.Equal("async", await image.GetAttributeAsync("decoding"));
            Assert.Equal("1536", await image.GetAttributeAsync("width"));
            Assert.Equal("1024", await image.GetAttributeAsync("height"));
            await image.EvaluateAsync("image => image.decode()");
            Assert.True(await image.EvaluateAsync<bool>("image => image.naturalWidth > 0 && image.naturalHeight > 0 && image.alt.trim().length > 0"));
            Assert.True(await stage.EvaluateAsync<bool>("stage => { const box = stage.getBoundingClientRect(); return box.left >= -1 && box.right <= innerWidth + 1; }"));
            if (reducedMotion || !javaScript)
            {
                Assert.Equal("none", await stage.EvaluateAsync<string>("stage => getComputedStyle(stage).transform"));
                // The application shell caps reduced-motion transitions at .01ms.
                Assert.InRange(await stage.EvaluateAsync<double>("stage => Math.max(...getComputedStyle(stage).transitionDuration.split(',').map(value => parseFloat(value)))"),
                    0, reducedMotion ? .00001 : 0);
            }
        }

        var columns = await workflow.Locator(".scanning-workflow-steps").EvaluateAsync<int>("list => getComputedStyle(list).gridTemplateColumns.split(' ').length");
        Assert.Equal(width < 768 ? 1 : width < 1024 ? 2 : 5, columns);
        var rail = workflow.Locator(".scanning-workflow-steps");
        Assert.Equal(width == 960 ? "none" : "block", await rail.EvaluateAsync<string>("list => getComputedStyle(list, '::before').display"));
        if (width != 960)
        {
            Assert.Equal("matrix(1, 0, 0, 1, 0, 0)", await rail.EvaluateAsync<string>("list => getComputedStyle(list, '::before').transform"));
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        var deliverables = page.Locator("#scanning-deliverables .scanning-deliverable-card");
        Assert.Equal(4, await deliverables.CountAsync());
        Assert.Equal("/src/images/services/scanning/art/scanning-art-clean-mesh.webp", await deliverables.Nth(1).Locator("img").GetAttributeAsync("src"));
        foreach (var image in await deliverables.Locator("img").AllAsync())
        {
            Assert.Equal("lazy", await image.GetAttributeAsync("loading"));
            Assert.NotNull(await image.GetAttributeAsync("width"));
            Assert.NotNull(await image.GetAttributeAsync("height"));
            await image.ScrollIntoViewIfNeededAsync();
            await image.EvaluateAsync("image => image.decode()");
            Assert.True(await image.EvaluateAsync<bool>("image => image.naturalWidth > 0 && image.alt.trim().length > 0"));
        }
    }
}
