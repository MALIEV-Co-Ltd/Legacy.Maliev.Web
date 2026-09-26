using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(ErrorBrowserCollection.Name)]
public sealed class ErrorBrowserTests(ErrorBrowserFixture fixture)
{
    private const string AstronautSelector = "img.space-error__astronaut.is-interactive";

    [Theory]
    [InlineData("th", 320, 740)]
    [InlineData("en", 320, 740)]
    [InlineData("th", 390, 844)]
    [InlineData("en", 390, 844)]
    [InlineData("th", 768, 1024)]
    [InlineData("en", 768, 1024)]
    [InlineData("th", 1920, 1080)]
    [InlineData("en", 1920, 1080)]
    [InlineData("th", 844, 390)]
    [InlineData("en", 844, 390)]
    public async Task NotFound_FillsViewportAndKeepsLocalizedRecoveryReachable(string culture, int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(ErrorUrl(404, culture));

        Assert.Equal(404, response?.Status);
        Assert.Equal(1, await page.Locator("main.space-error").CountAsync());
        Assert.Equal(0, await page.Locator("nav, footer").CountAsync());
        Assert.Contains("404", await page.Locator("h1").InnerTextAsync());
        Assert.Contains(culture == "th" ? "ไม่พบหน้าที่ต้องการ" : "Page not found", await page.Locator("main").InnerTextAsync());
        Assert.Equal("/", await page.Locator("a.space-error__home").GetAttributeAsync("href"));
        Assert.Equal("/Contact", await page.Locator("a.space-error__support").GetAttributeAsync("href"));
        Assert.True(await page.EvaluateAsync<bool>(@"() => {
            const images = Array.from(document.querySelectorAll('picture.space-error__background img, img.space-error__astronaut'));
            return images.length === 2 && images.every(image => image.complete && image.naturalWidth > 0);
        }"));
        Assert.True(await page.EvaluateAsync<bool>(@"() => {
            const main = document.querySelector('main.space-error').getBoundingClientRect();
            return document.documentElement.scrollWidth <= innerWidth + 1
                && document.documentElement.scrollHeight <= innerHeight + 1
                && Math.abs(main.width - innerWidth) <= 1 && Math.abs(main.height - innerHeight) <= 1;
        }"));
        Assert.True(await page.EvaluateAsync<bool>(@"() =>
            Array.from(document.querySelectorAll('.space-error__home, .space-error__support')).every(link => {
                const box = link.getBoundingClientRect();
                const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
                return box.left >= 0 && box.top >= 0 && box.right <= innerWidth && box.bottom <= innerHeight
                    && box.width >= 44 && box.height >= 44 && (hit === link || link.contains(hit));
            })"));
    }

    [Fact]
    public async Task NotFound_WithoutJavaScriptKeepsRecoveryUsable()
    {
        await using var context = await CreateContextAsync(390, 844, javaScriptEnabled: false);
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(ErrorUrl(404, "en"));

        Assert.Equal(404, response?.Status);
        Assert.True(await page.Locator("a.space-error__home").IsVisibleAsync());
        Assert.True(await page.Locator("a.space-error__support").IsVisibleAsync());
        Assert.Equal(0, await page.Locator("button.space-error__motion").CountAsync());
        await page.Locator("a.space-error__home").ClickAsync();
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
    }

    [Fact]
    public async Task NotFound_MotionRespondsToPreferenceWithoutPlaybackControls()
    {
        await using var context = await CreateContextAsync(1920, 1080);
        await using var page = await OpenNotFoundAsync(context);
        await AssertMovingAsync(page);
        Assert.Equal(0, await page.Locator("button").CountAsync());

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await AssertStillAsync(page);
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.NoPreference });
        await AssertMovingAsync(page);
    }

    [Fact]
    public async Task NotFound_ReducedMotionStartsStill()
    {
        await using var context = await CreateContextAsync(390, 844, reducedMotion: ReducedMotion.Reduce);
        await using var page = await OpenNotFoundAsync(context);
        Assert.True(await page.Locator(AstronautSelector).IsVisibleAsync());
        await AssertStillAsync(page);
        Assert.True(await page.Locator("a.space-error__home").IsVisibleAsync());
    }

    [Fact]
    public async Task Astronaut_MouseDragThrowsAndReboundsWithinViewport()
    {
        await using var context = await CreateContextAsync(1440, 900);
        await using var page = await OpenNotFoundAsync(context);
        var astronaut = page.Locator(AstronautSelector);
        Assert.Equal("grab", await astronaut.EvaluateAsync<string>("element => getComputedStyle(element).cursor"));
        var box = Assert.IsType<LocatorBoundingBoxResult>(await astronaut.BoundingBoxAsync());
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        Assert.Equal("dragging", await astronaut.GetAttributeAsync("data-motion"));
        Assert.Equal("grabbing", await astronaut.EvaluateAsync<string>("element => getComputedStyle(element).cursor"));
        await page.Mouse.MoveAsync(x + 220, y, new MouseMoveOptions { Steps = 8 });
        var dragged = Assert.IsType<LocatorBoundingBoxResult>(await astronaut.BoundingBoxAsync());
        Assert.True(dragged.X - box.X > 200);
        await page.Mouse.MoveAsync(x + 300, y, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
        Assert.Equal("thrown", await astronaut.GetAttributeAsync("data-motion"));
        Assert.Equal("grab", await astronaut.EvaluateAsync<string>("element => getComputedStyle(element).cursor"));
        Assert.True(await page.EvaluateAsync<bool>(@"async () => {
            const astronaut = document.querySelector('.space-error__astronaut');
            let previous = astronaut.getBoundingClientRect();
            let movedRight = false;
            let rebounded = false;
            for (let frame = 0; frame < 120; frame++) {
                await new Promise(requestAnimationFrame);
                const box = astronaut.getBoundingClientRect();
                if (box.left < 7 || box.top < 7 || box.right > innerWidth - 7 || box.bottom > innerHeight - 7) return false;
                if (box.x > previous.x + 1) movedRight = true;
                if (movedRight && box.x < previous.x - 1) rebounded = true;
                previous = box;
            }
            return movedRight && rebounded;
        }"));
    }

    [Fact]
    public async Task Astronaut_ReducedMotionDragStaysAtReleasePosition()
    {
        await using var context = await CreateContextAsync(1440, 900, reducedMotion: ReducedMotion.Reduce);
        await using var page = await OpenNotFoundAsync(context);
        var astronaut = page.Locator(AstronautSelector);
        var box = Assert.IsType<LocatorBoundingBoxResult>(await astronaut.BoundingBoxAsync());
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + 200, y + 80, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();

        Assert.Equal("still", await astronaut.GetAttributeAsync("data-motion"));
        var released = Assert.IsType<LocatorBoundingBoxResult>(await astronaut.BoundingBoxAsync());
        Assert.InRange(released.X - box.X, 199, 201);
        Assert.InRange(released.Y - box.Y, 79, 81);
        await AssertStillAsync(page);
    }

    [Fact]
    public async Task Astronaut_TouchDragThrowsWithoutScrolling()
    {
        await using var context = await CreateContextAsync(390, 844, hasTouch: true);
        await using var page = await OpenNotFoundAsync(context);
        var astronaut = page.Locator(AstronautSelector);
        var box = Assert.IsType<LocatorBoundingBoxResult>(await astronaut.BoundingBoxAsync());
        double x = box.X + box.Width / 2;
        double y = box.Y + box.Height / 2;
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new[] { new { x, y } },
        });
        Assert.Equal("dragging", await astronaut.GetAttributeAsync("data-motion"));
        for (var step = 1; step <= 5; step++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchMove",
                ["touchPoints"] = new[] { new { x = x + step * 16, y = y + step * 12 } },
            });
            await page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
        }

        var dragged = Assert.IsType<LocatorBoundingBoxResult>(await astronaut.BoundingBoxAsync());
        Assert.InRange(dragged.X - box.X, 78, 82);
        Assert.InRange(dragged.Y - box.Y, 58, 62);
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchEnd",
            ["touchPoints"] = Array.Empty<object>(),
        });
        Assert.Equal("thrown", await astronaut.GetAttributeAsync("data-motion"));
        Assert.True(await page.EvaluateAsync<bool>(@"async () => {
            const astronaut = document.querySelector('.space-error__astronaut');
            const initial = astronaut.getBoundingClientRect();
            let moved = false;
            for (let frame = 0; frame < 30; frame++) {
                await new Promise(requestAnimationFrame);
                const box = astronaut.getBoundingClientRect();
                if (Math.hypot(box.x - initial.x, box.y - initial.y) > 8) moved = true;
                if (box.left < 7 || box.top < 7 || box.right > innerWidth - 7 || box.bottom > innerHeight - 7) return false;
            }
            return moved && scrollX === 0 && scrollY === 0;
        }"));
    }

    [Fact]
    public async Task Astronaut_KeyboardNudgesAndResizeKeepsItVisible()
    {
        await using var context = await CreateContextAsync(1440, 900, reducedMotion: ReducedMotion.Reduce);
        await using var page = await OpenNotFoundAsync(context);
        var astronaut = page.Locator(AstronautSelector);
        Assert.False(string.IsNullOrWhiteSpace(await astronaut.GetAttributeAsync("aria-label")));
        await astronaut.FocusAsync();
        Assert.True(await astronaut.EvaluateAsync<bool>("element => document.activeElement === element"));
        var initial = Assert.IsType<LocatorBoundingBoxResult>(await astronaut.BoundingBoxAsync());
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        var moved = Assert.IsType<LocatorBoundingBoxResult>(await astronaut.BoundingBoxAsync());
        Assert.InRange(moved.X - initial.X, 23, 25);
        Assert.InRange(moved.Y - initial.Y, 47, 49);
        Assert.Equal("still", await astronaut.GetAttributeAsync("data-motion"));

        await page.SetViewportSizeAsync(320, 740);
        await page.WaitForFunctionAsync(@"() => {
            const box = document.querySelector('.space-error__astronaut').getBoundingClientRect();
            return box.left >= 7 && box.top >= 7 && box.right <= innerWidth - 7 && box.bottom <= innerHeight - 7;
        }");
        Assert.True(await page.Locator("a.space-error__home").IsVisibleAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("scrollX + scrollY"));
    }

    private static async Task AssertMovingAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.querySelector('.space-error__astronaut').dataset.motion === 'floating'");
        var transform = await page.Locator(".space-error__astronaut").EvaluateAsync<string>("element => element.style.transform");
        await page.WaitForFunctionAsync("previous => document.querySelector('.space-error__astronaut').style.transform !== previous", transform);
    }

    private static async Task AssertStillAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.querySelector('.space-error__astronaut').dataset.motion === 'still'");
        var transform = await page.Locator(".space-error__astronaut").EvaluateAsync<string>("element => element.style.transform");
        await page.WaitForTimeoutAsync(200);
        Assert.Equal(transform, await page.Locator(".space-error__astronaut").EvaluateAsync<string>("element => element.style.transform"));
    }

    private Task<IBrowserContext> CreateContextAsync(int width, int height, bool javaScriptEnabled = true,
        ReducedMotion reducedMotion = ReducedMotion.NoPreference, bool hasTouch = false) =>
        fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            JavaScriptEnabled = javaScriptEnabled,
            ReducedMotion = reducedMotion,
            HasTouch = hasTouch,
            IsMobile = hasTouch,
        });

    private async Task<IPage> OpenNotFoundAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(ErrorUrl(404, "en"));
        Assert.Equal(404, response?.Status);
        await page.WaitForSelectorAsync(AstronautSelector);
        return page;
    }

    private string ErrorUrl(int code, string culture) => new Uri(fixture.Origin, $"/error?code={code}&culture={culture}").ToString();
}

public sealed class ErrorBrowserFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? factory;
    private IPlaywright? playwright;

    public IBrowser Browser { get; private set; } = null!;
    public Uri Origin { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Origin = new Uri($"http://127.0.0.1:{port}");

        factory = new TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = Origin,
        });
        using var response = await client.GetAsync("/error?code=404&culture=en");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        playwright?.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ErrorBrowserCollection : ICollectionFixture<ErrorBrowserFixture>
{
    public const string Name = "Error browser";
}
