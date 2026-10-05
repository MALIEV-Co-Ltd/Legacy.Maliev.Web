using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

// Historical gallery geometry only; no CNC engine, upload or quotation behavior.
[Collection(CncNativeBrowserCollection.Name)]
public sealed class ServiceGalleryHistoricalBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public async Task PrintingGallery_ButtonHasReadableContrast_AndExpandedImagesFillVariedTiles()
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "th-TH",
                ViewportSize = new ViewportSize { Width = 1269, Height = 1032 },
            });
        IPage page = await context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        string origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);

        var response = await page.GotoAsync(
            origin + "/services/3d-printing#printing-part-proof",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"3D-printing service page returned HTTP {response.Status}.");
        await AssertActiveRendererAsync(page);

        double contrastRatio = await page.Locator("[data-part-gallery-toggle]").EvaluateAsync<double>(
            @"element => {
                const parse = value => value.match(/[\d.]+/g).slice(0, 3).map(Number);
                const luminance = rgb => {
                    const linear = rgb.map(channel => {
                        const value = channel / 255;
                        return value <= .04045 ? value / 12.92 : Math.pow((value + .055) / 1.055, 2.4);
                    });
                    return .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2];
                };
                const style = getComputedStyle(element);
                const foreground = luminance(parse(style.color));
                const background = luminance(parse(style.backgroundColor));
                return (Math.max(foreground, background) + .05) / (Math.min(foreground, background) + .05);
            }");
        Assert.True(contrastRatio >= 4.5, $"Gallery disclosure contrast was only {contrastRatio:F2}:1.");

        await RejectOptionalCookiesAsync(page);
        await page.ClickAsync("[data-part-gallery-toggle]");
        await DecodeGalleryAsync(page);
        string[] layouts = await page.Locator("[data-part-gallery-extra] .service-part-bento-media")
            .EvaluateAllAsync<string[]>(@"elements => elements.map(element => {
                const figure = element.closest('figure');
                const tile = figure.getBoundingClientRect();
                const style = getComputedStyle(figure);
                const borderWidths = [style.borderTopWidth, style.borderRightWidth, style.borderBottomWidth, style.borderLeftWidth].join(',');
                return getComputedStyle(element).objectFit + '|' + (tile.width / tile.height).toFixed(1) + '|' + borderWidths;
            })");

        Assert.Equal(12, layouts.Length);
        Assert.All(layouts, layout => Assert.StartsWith("cover|", layout, StringComparison.Ordinal));
        Assert.All(layouts, layout => Assert.EndsWith("|0px,0px,0px,0px", layout, StringComparison.Ordinal));
        Assert.True(
            layouts.Select(layout => layout.Split('|')[1]).Distinct(StringComparer.Ordinal).Count() >= 4,
            "Expanded production images collapsed into an equal-card grid instead of a varied Bento composition.");

        string[] portraitTiles =
        {
            ".service-part-bento-tile--pla",
            ".service-part-bento-tile--rubber",
            ".service-part-bento-tile--petgshell",
            ".service-part-bento-tile--tpuhandle",
        };
        foreach (string selector in portraitTiles)
        {
            double ratio = await page.Locator($"#printing-part-gallery-extra {selector}")
                .EvaluateAsync<double>("element => element.getBoundingClientRect().width / element.getBoundingClientRect().height");
            Assert.True(ratio < 0.9, $"{selector} should be framed as a portrait Bento tile, but its ratio was {ratio:F2}.");
        }

        double hipsRatio = await page.Locator("#printing-part-gallery-extra .service-part-bento-tile--hips")
            .EvaluateAsync<double>("element => element.getBoundingClientRect().width / element.getBoundingClientRect().height");
        Assert.True(hipsRatio > 1.1, $"The HIPS sample should be framed as a wide Bento tile, but its ratio was {hipsRatio:F2}.");
    }

    [Theory]
    [InlineData(1269, 1032)]
    [InlineData(900, 1000)]
    [InlineData(390, 844)]
    public async Task PrintingGallery_ExpandedPartsRemainProportionateAndMostlyVisible(
        int viewportWidth,
        int viewportHeight)
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "th-TH",
                ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight },
            });
        IPage page = await context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        string origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);

        var response = await page.GotoAsync(
            origin + "/services/3d-printing#printing-part-proof",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"3D-printing service page returned HTTP {response.Status}.");
        await AssertActiveRendererAsync(page);

        await RejectOptionalCookiesAsync(page);
        await page.ClickAsync("[data-part-gallery-toggle]");
        await DecodeGalleryAsync(page);


        string[] framingStates = await page.Locator("#printing-part-gallery-extra .service-part-bento-media")
            .EvaluateAllAsync<string[]>(@"images => images.map(image => {
                const tile = image.closest('figure');
                const tileRect = tile.getBoundingClientRect();
                const sourceRatio = image.naturalWidth / image.naturalHeight;
                const tileRatio = tileRect.width / tileRect.height;
                const visibleFraction = Math.min(sourceRatio / tileRatio, tileRatio / sourceRatio);
                const tileName = Array.from(tile.classList).find(name => name.startsWith('service-part-bento-tile--'));
                return `${tileName}|${getComputedStyle(image).objectFit}|${visibleFraction.toFixed(3)}`;
            })");

        Assert.Equal(12, framingStates.Length);
        foreach (string framingState in framingStates)
        {
            string[] fields = framingState.Split('|');
            Assert.Equal("cover", fields[1]);
            Assert.True(
                double.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture) >= 0.72,
                $"{fields[0]} preserves only {fields[2]} of its source frame at {viewportWidth}px.");
        }
    }

    [Fact]
    public async Task PrintingGallery_TabletLeadCompositionPreservesTheHeroPart()
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "th-TH",
                ViewportSize = new ViewportSize { Width = 940, Height = 1032 },
            });
        IPage page = await context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        string origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);

        var response = await page.GotoAsync(
            origin + "/services/3d-printing#printing-part-proof",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"3D-printing service page returned HTTP {response.Status}.");
        await AssertActiveRendererAsync(page);

        string framingState = await page.Locator(".service-part-bento-tile--hero")
            .EvaluateAsync<string>(@"tile => {
                const image = tile.querySelector('img');
                const rect = tile.getBoundingClientRect();
                const sourceRatio = Number(image.getAttribute('width')) / Number(image.getAttribute('height'));
                const tileRatio = rect.width / rect.height;
                const visibleFraction = Math.min(sourceRatio / tileRatio, tileRatio / sourceRatio);
                return `${tileRatio.toFixed(3)}|${visibleFraction.toFixed(3)}`;
            }");

        string[] fields = framingState.Split('|');
        double tileRatio = double.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture);
        double visibleFraction = double.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(tileRatio > 1.3, $"The lead tablet tile should be landscape, but its ratio was {tileRatio:F3}.");
        Assert.True(visibleFraction >= 0.85, $"The lead tablet tile preserves only {visibleFraction:F3} of its source frame.");

        string[] openingFramingStates = await page.Locator(
            "#printing-part-proof .service-part-bento:not(.service-part-bento--expanded) .service-part-bento-tile")
            .EvaluateAllAsync<string[]>(@"tiles => tiles.map(tile => {
                const image = tile.querySelector('img');
                const rect = tile.getBoundingClientRect();
                const sourceRatio = Number(image.getAttribute('width')) / Number(image.getAttribute('height'));
                const tileRatio = rect.width / rect.height;
                const visibleFraction = Math.min(sourceRatio / tileRatio, tileRatio / sourceRatio);
                const name = Array.from(tile.classList).find(value => value.startsWith('service-part-bento-tile--'));
                return `${name}|${visibleFraction.toFixed(3)}`;
            })");

        Assert.Equal(8, openingFramingStates.Length);
        foreach (string openingFramingState in openingFramingStates)
        {
            string[] openingFields = openingFramingState.Split('|');
            double openingVisibleFraction = double.Parse(openingFields[1], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(openingVisibleFraction >= 0.7, $"{openingFields[0]} preserves only {openingVisibleFraction:F3} of its tablet source frame.");
        }
    }

    [Theory]
    [InlineData("/services/3d-printing#printing-part-proof", "#printing-part-proof", ".service-part-bento:not(.service-part-bento--expanded)", ".service-part-bento--expanded", 320, 2)]
    [InlineData("/services/3d-printing#printing-part-proof", "#printing-part-proof", ".service-part-bento:not(.service-part-bento--expanded)", ".service-part-bento--expanded", 390, 3)]
    [InlineData("/services/3d-printing#printing-part-proof", "#printing-part-proof", ".service-part-bento:not(.service-part-bento--expanded)", ".service-part-bento--expanded", 430, 3)]
    [InlineData("/services/cnc-machining#cnc-part-proof", "#cnc-part-proof", ".service-part-bento--cnc:not(.service-part-bento--cnc-expanded)", ".service-part-bento--cnc-expanded", 320, 2)]
    [InlineData("/services/cnc-machining#cnc-part-proof", "#cnc-part-proof", ".service-part-bento--cnc:not(.service-part-bento--cnc-expanded)", ".service-part-bento--cnc-expanded", 390, 3)]
    [InlineData("/services/cnc-machining#cnc-part-proof", "#cnc-part-proof", ".service-part-bento--cnc:not(.service-part-bento--cnc-expanded)", ".service-part-bento--cnc-expanded", 430, 3)]
    public async Task ServicePartGallery_MobileTilesPreserveBentoComposition(
        string route,
        string sectionSelector,
        string openingSelector,
        string expandedSelector,
        int viewportWidth,
        int expectedColumnCount)
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "th-TH",
                ViewportSize = new ViewportSize { Width = viewportWidth, Height = 844 },
            });
        IPage page = await context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        string origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);

        var response = await page.GotoAsync(
            origin + route,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Service page returned HTTP {response.Status}.");
        await AssertActiveRendererAsync(page);

        async Task AssertBentoLayout(string gallerySelector)
        {
            string layoutState = await page.Locator(sectionSelector + " " + gallerySelector)
                .EvaluateAsync<string>(@"grid => {
                const gridRect = grid.getBoundingClientRect();
                const tiles = Array.from(grid.children);
                const narrowestTile = Math.min(...tiles.map(tile => tile.getBoundingClientRect().width));
                const widestTile = Math.max(...tiles.map(tile => tile.getBoundingClientRect().width));
                const gridStyle = getComputedStyle(grid);
                const columns = gridStyle.gridTemplateColumns.split(' ').map(value => parseFloat(value));
                const columnCount = columns.length;
                const columnGap = parseFloat(gridStyle.columnGap);
                const rowGap = parseFloat(gridStyle.rowGap);
                const explicitRows = gridStyle.gridTemplateRows.split(' ').map(value => parseFloat(value));
                const rowHeight = explicitRows.length > 0 ? explicitRows[0] : parseFloat(gridStyle.gridAutoRows);
                const overflows = tiles.some(tile => {
                    const rect = tile.getBoundingClientRect();
                    return rect.left < gridRect.left - 1 || rect.right > gridRect.right + 1;
                });
                const occupiedCells = new Set();
                let rowCount = 0;
                for (const tile of tiles) {
                    const rect = tile.getBoundingClientRect();
                    const column = Math.round((rect.left - gridRect.left) / (columns[0] + columnGap));
                    const columnSpan = Math.round((rect.width + columnGap) / (columns[0] + columnGap));
                    const row = Math.round((rect.top - gridRect.top) / (rowHeight + rowGap));
                    const rowSpan = Math.round((rect.height + rowGap) / (rowHeight + rowGap));
                    rowCount = Math.max(rowCount, row + rowSpan);

                    for (let occupiedRow = row; occupiedRow < row + rowSpan; occupiedRow++) {
                        for (let occupiedColumn = column; occupiedColumn < column + columnSpan; occupiedColumn++) {
                            occupiedCells.add(`${occupiedRow}:${occupiedColumn}`);
                        }
                    }
                }

                const emptyCellCount = (rowCount * columnCount) - occupiedCells.size;
                return `${columnCount}|${gridRect.width.toFixed(3)}|${narrowestTile.toFixed(3)}|${widestTile.toFixed(3)}|${overflows}|${emptyCellCount}`;
            }");

            string[] fields = layoutState.Split('|');
            int columnCount = int.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture);
            double gridWidth = double.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            double narrowestTile = double.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture);
            double widestTile = double.Parse(fields[3], System.Globalization.CultureInfo.InvariantCulture);
            bool overflows = bool.Parse(fields[4]);
            int emptyCellCount = int.Parse(fields[5], System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(expectedColumnCount, columnCount);
            Assert.True(widestTile >= gridWidth * 0.6, $"The widest mobile tile was only {widestTile:F1}px in a {gridWidth:F1}px grid; no tile spans multiple columns.");
            Assert.True(narrowestTile <= gridWidth * 0.6, $"The narrowest mobile tile was {narrowestTile:F1}px in a {gridWidth:F1}px grid; the composition is not varied.");
            Assert.False(overflows, "A mobile bento tile overflowed its grid.");
            Assert.Equal(0, emptyCellCount);
        }

        await AssertBentoLayout(openingSelector);
        await RejectOptionalCookiesAsync(page);
        await page.ClickAsync($"{sectionSelector} [data-part-gallery-toggle]");
        await DecodeGalleryAsync(page);
        await AssertBentoLayout(expandedSelector);
    }

    [Fact]
    public async Task CncGallery_PreservesCriticalPartFraming()
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "th-TH",
                ViewportSize = new ViewportSize { Width = 1172, Height = 1032 },
            });
        IPage page = await context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        string origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);

        var response = await page.GotoAsync(
            origin + "/services/cnc-machining#cnc-part-proof",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"CNC service page returned HTTP {response.Status}.");
        await AssertActiveRendererAsync(page);

        await RejectOptionalCookiesAsync(page);
        await page.ClickAsync("[data-part-gallery-toggle]");
        await DecodeGalleryAsync(page);
        const string criticalTiles = ".service-part-bento-tile--cnc-controller, .service-part-bento-tile--cnc-shaft, .service-part-bento-tile--cnc-stainless";
        ILocator criticalTileLocator = page.Locator(criticalTiles);
        await criticalTileLocator.Last.ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync(
            "selector => Array.from(document.querySelectorAll(selector)).every(tile => { const image = tile.querySelector('img'); return image?.complete && image.naturalWidth > 0; })",
            criticalTiles);

        string[] framingStates = await criticalTileLocator
            .EvaluateAllAsync<string[]>(@"tiles => tiles.map(tile => {
                const image = tile.querySelector('img');
                const rect = tile.getBoundingClientRect();
                const sourceRatio = image.naturalWidth / image.naturalHeight;
                const tileRatio = rect.width / rect.height;
                const visibleFraction = Math.min(sourceRatio / tileRatio, tileRatio / sourceRatio);
                const name = Array.from(tile.classList).find(value => value.startsWith('service-part-bento-tile--cnc-'));
                return `${name}|${tileRatio.toFixed(3)}|${visibleFraction.toFixed(3)}`;
            })");

        Assert.Equal(3, framingStates.Length);
        foreach (string framingState in framingStates)
        {
            string[] fields = framingState.Split('|');
            double tileRatio = double.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            double visibleFraction = double.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(tileRatio > 1.3, $"{fields[0]} should use a horizontal Bento frame, but its ratio was {tileRatio:F2}.");
            double requiredFraction = fields[0].EndsWith("controller", StringComparison.Ordinal) ? 0.9 : 0.82;
            Assert.True(visibleFraction >= requiredFraction, $"{fields[0]} preserves only {visibleFraction:F3} of its source frame.");
        }

        await page.SetViewportSizeAsync(390, 844);
        await DecodeGalleryAsync(page);
        string[] mobileFramingStates = await page.Locator("#cnc-part-proof .service-part-bento-tile")
            .EvaluateAllAsync<string[]>(@"tiles => tiles.map(tile => {
                const image = tile.querySelector('img');
                const rect = tile.getBoundingClientRect();
                const sourceRatio = image.naturalWidth / image.naturalHeight;
                const tileRatio = rect.width / rect.height;
                const visibleFraction = Math.min(sourceRatio / tileRatio, tileRatio / sourceRatio);
                const name = Array.from(tile.classList).find(value => value.startsWith('service-part-bento-tile--cnc-'));
                return `${name}|${visibleFraction.toFixed(3)}`;
            })");

        Assert.Equal(19, mobileFramingStates.Length);
        foreach (string framingState in mobileFramingStates)
        {
            string[] fields = framingState.Split('|');
            double visibleFraction = double.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(visibleFraction >= 0.8, $"{fields[0]} preserves only {visibleFraction:F3} of its source frame on mobile.");
        }
    }

    [Fact]
    public async Task CncGallery_TabletScalePartUsesHorizontalFrame()
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "th-TH",
                ViewportSize = new ViewportSize { Width = 910, Height = 1032 },
            });
        IPage page = await context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        string origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);

        var response = await page.GotoAsync(
            origin + "/services/cnc-machining#cnc-part-proof",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"CNC service page returned HTTP {response.Status}.");
        await AssertActiveRendererAsync(page);

        await RejectOptionalCookiesAsync(page);
        await page.ClickAsync("[data-part-gallery-toggle]");
        await DecodeGalleryAsync(page);
        string framingState = await page.Locator(".service-part-bento-tile--cnc-scale")
            .EvaluateAsync<string>(@"tile => {
                const image = tile.querySelector('img');
                const rect = tile.getBoundingClientRect();
                const sourceRatio = Number(image.getAttribute('width')) / Number(image.getAttribute('height'));
                const tileRatio = rect.width / rect.height;
                const visibleFraction = Math.min(sourceRatio / tileRatio, tileRatio / sourceRatio);
                return `${tileRatio.toFixed(3)}|${visibleFraction.toFixed(3)}`;
            }");

        string[] fields = framingState.Split('|');
        double tileRatio = double.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture);
        double visibleFraction = double.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(tileRatio > 1.3, $"The CNC scale tablet tile should be horizontal, but its ratio was {tileRatio:F3}.");
        Assert.True(visibleFraction >= 0.8, $"The CNC scale tablet tile preserves only {visibleFraction:F3} of its source frame.");
    }

    private static async Task AssertActiveRendererAsync(IPage page)
    {
        Assert.Equal(1, await page.Locator(
            "main.service-page[data-migration-route-owner='blazor-static-ssr']").CountAsync());
        await page.WaitForFunctionAsync("() => document.fonts.status === 'loaded'", null,
            new PageWaitForFunctionOptions { Timeout = 30000 });
    }

    private static async Task RejectOptionalCookiesAsync(IPage page)
    {
        var reject = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await reject.IsVisibleAsync())
        {
            await reject.ClickAsync();
        }
    }

    private static async Task DecodeGalleryAsync(IPage page)
    {
        // Run after the real disclosure promotes deferred image sources.
        // Eager loading avoids a viewport-dependent wait for offscreen gallery photographs.
        await page.EvaluateAsync("""
            async () => {
                const images = Array.from(document.querySelectorAll('.service-part-bento-tile img'));
                if (!images.length) throw new Error('The active service gallery has no images.');
                if (images.some(image => image.hasAttribute('data-src') || image.hasAttribute('data-srcset'))) {
                    throw new Error('Gallery disclosure did not promote deferred image sources.');
                }
                images.forEach(image => image.loading = 'eager');
                let timer;
                try {
                    await Promise.race([
                        Promise.all(images.map(image => image.decode())),
                        new Promise((_, reject) => {
                            timer = setTimeout(() => reject(new Error('Gallery image decode exceeded 15 seconds.')), 15000);
                        })
                    ]);
                    if (images.some(image => image.naturalWidth <= 1 || image.naturalHeight <= 1)) {
                        throw new Error('Gallery image did not resolve to a production photograph.');
                    }
                } finally {
                    clearTimeout(timer);
                }
            }
            """).WaitAsync(TimeSpan.FromSeconds(20));
    }
}
