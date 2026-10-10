using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class CrawlableEmailFallbackBrowserTests(PublicContactBrowserFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData("en", "/legal", ".landing-footer-contact-row", "info@maliev.com")]
    [InlineData("th", "/legal", ".landing-footer-contact-row", "info@maliev.com")]
    [InlineData("en", "/legal/nondisclosureagreement", ".legal-document a", "nda@maliev.com")]
    [InlineData("th", "/legal/nondisclosureagreement", ".legal-document a", "nda@maliev.com")]
    [InlineData("en", "/contact", ".contact-method-card a", "info@maliev.com")]
    [InlineData("th", "/contact", ".contact-method-card a", "info@maliev.com")]
    public async Task NoJavascript_PublicEmailKeepsCrawlableContactFallback(
        string culture, string path, string selector, string recipient)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"{path}?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var anchor = page.Locator(selector).Filter(new() { HasText = recipient }).First;
        Assert.Equal(recipient, await anchor.InnerTextAsync());
        Assert.Equal("/contact#contact-us", await anchor.GetAttributeAsync("href"));
        Assert.Equal(recipient, await anchor.GetAttributeAsync("data-contact-email"));
        using var navigation = new ContactFallbackNavigationEvidence(page, fixture.Origin);
        await ContactFallbackNavigationEvidence.PreserveFailureAsync(async () =>
        {
            await anchor.ClickAsync();
            await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/contact" && new Uri(url).Fragment == "#contact-us");
            Assert.Equal(1, await page.Locator("#contact-us").CountAsync());
        }, () => navigation.WriteFailure(output, culture));
    }

    [Theory]
    [InlineData("en", "info@maliev.com")]
    [InlineData("th", "info@maliev.com")]
    [InlineData("en", "manufacturing@maliev.com")]
    [InlineData("th", "nda@maliev.com")]
    [InlineData("en", "career@maliev.com")]
    [InlineData("th", "support@maliev.com")]
    public async Task RenderedContactScript_EnhancesKnownRecipientWithoutAnalyticsPii(string culture, string recipient)
    {
        var result = await ExecuteRenderedScript(culture, recipient);
        Assert.True(result.GetProperty("prevented").GetBoolean());
        Assert.Equal("mailto:" + recipient, result.GetProperty("destinations")[0].GetString());
        var diagnostic = Assert.Single(result.GetProperty("events").EnumerateArray());
        Assert.Equal("email_click", diagnostic.GetProperty("event").GetString());
        Assert.Equal("email", diagnostic.GetProperty("channel").GetString());
        Assert.Equal("business_email", diagnostic.GetProperty("destination").GetString());
        Assert.Equal("contact", diagnostic.GetProperty("context").GetString());
        Assert.Equal(new[] { "channel", "context", "destination", "event" },
            diagnostic.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(recipient, diagnostic.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.GetProperty("dataLayerWrites").GetInt32());
    }

    [Theory]
    [InlineData("unknown@example.test")]
    [InlineData("info@maliev.com?subject=private")]
    [InlineData("__proto__")]
    public async Task RenderedContactScript_UnknownMarkerLeavesFallbackUntouched(string marker)
    {
        var result = await ExecuteRenderedScript("en", marker);
        Assert.False(result.GetProperty("prevented").GetBoolean());
        Assert.Empty(result.GetProperty("destinations").EnumerateArray());
        Assert.Empty(result.GetProperty("events").EnumerateArray());
        Assert.Equal(0, result.GetProperty("dataLayerWrites").GetInt32());
    }

    [Theory]
    [InlineData(" INFO@MALIEV.COM ")]
    [InlineData("\tCareer@Maliev.Com\n")]
    public async Task RenderedContactScript_NormalizesKnownRecipient(string marker)
    {
        var result = await ExecuteRenderedScript("en", marker);
        Assert.True(result.GetProperty("prevented").GetBoolean());
        Assert.Equal("mailto:" + marker.Trim().ToLowerInvariant(),
            Assert.Single(result.GetProperty("destinations").EnumerateArray()).GetString());
        Assert.Single(result.GetProperty("events").EnumerateArray());
    }

    [Theory]
    [InlineData("ctrl")]
    [InlineData("meta")]
    [InlineData("shift")]
    [InlineData("alt")]
    [InlineData("middle")]
    [InlineData("prevented")]
    [InlineData("download")]
    public async Task RenderedContactScript_PreservesUnownedClick(string mode)
    {
        var result = await ExecuteRenderedScript("en", "info@maliev.com", mode);
        Assert.False(result.GetProperty("prevented").GetBoolean());
        Assert.Empty(result.GetProperty("destinations").EnumerateArray());
        Assert.Empty(result.GetProperty("events").EnumerateArray());
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("denied")]
    public async Task RenderedContactScript_AnalyticsUnavailableDoesNotBlockMailto(string mode)
    {
        var result = await ExecuteRenderedScript("en", "info@maliev.com", mode);
        Assert.True(result.GetProperty("prevented").GetBoolean());
        Assert.Equal("mailto:info@maliev.com",
            Assert.Single(result.GetProperty("destinations").EnumerateArray()).GetString());
        Assert.Empty(result.GetProperty("events").EnumerateArray());
        Assert.Equal(0, result.GetProperty("dataLayerWrites").GetInt32());
    }

    private async Task<JsonElement> ExecuteRenderedScript(string culture, string marker, string mode = "normal")
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var response = await context.APIRequest.GetAsync(new Uri(fixture.Origin, $"/contact?culture={culture}").ToString());
        Assert.True(response.Ok);
        var html = await response.TextAsync();
        var match = Regex.Match(html,
            "<script[^>]*data-migration-component=\"public-contact-channel-analytics\"[^>]*>(?<script>[\\s\\S]*?)</script>");
        Assert.True(match.Success);
        await using var page = await context.NewPageAsync();
        await page.SetContentAsync($"<base href='{fixture.Origin}contact'><a href='/contact#contact-us'>Public contact</a>");
        var json = await page.EvaluateAsync<string>("""
            argument => {
                const events = [], destinations = [], listeners = [];
                const anchor = document.querySelector('a');
                anchor.setAttribute('data-contact-email', argument.marker);
                if (argument.mode === 'download') anchor.setAttribute('download', '');
                const testDocument = {
                    baseURI: document.baseURI,
                    addEventListener: (name, callback) => {
                        if (name === 'click') listeners.push(callback);
                    }
                };
                let dataLayerWrites = 0, prevented = false;
                const testWindow = {
                    location: { hostname: new URL(document.baseURI).hostname,
                        pathname: '/contact', assign: target => destinations.push(target) },
                    setTimeout: callback => callback(),
                    malievAnalytics: { emit: value => events.push(value) },
                    dataLayer: { push: () => dataLayerWrites++ }
                };
                if (argument.mode === 'unavailable') delete testWindow.malievAnalytics;
                if (argument.mode === 'denied') testWindow.malievAnalytics.emit = () => false;
                new Function('window', 'document', argument.script)(testWindow, testDocument);
                if (listeners.length !== 1) throw new Error('Expected one rendered contact handler.');
                listeners[0]({ target: anchor, button: argument.mode === 'middle' ? 1 : 0,
                    defaultPrevented: argument.mode === 'prevented',
                    metaKey: argument.mode === 'meta', ctrlKey: argument.mode === 'ctrl',
                    shiftKey: argument.mode === 'shift', altKey: argument.mode === 'alt',
                    preventDefault: () => { prevented = true; } });
                return JSON.stringify({ events, destinations, prevented, dataLayerWrites });
            }
            """, new { marker, mode, script = match.Groups["script"].Value });
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

// Failure-only bounded network metadata; no headers, queries, bodies, raw URLs or exception text.
internal sealed class ContactFallbackNavigationEvidence : IDisposable
{
    private readonly IPage page;
    private readonly Uri origin;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly ConcurrentQueue<object> events = new();
    private readonly object gate = new();
    private int observed;
    private bool disposed;
    internal int DetachFailures { get; private set; }

    internal ContactFallbackNavigationEvidence(IPage page, Uri origin)
    {
        this.page = page;
        this.origin = origin;
        try
        {
            page.Request += Started;
            page.Response += Responded;
            page.RequestFinished += Finished;
            page.RequestFailed += Failed;
            page.FrameNavigated += Navigated;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static async Task PreserveFailureAsync(Func<Task> operation, Action observeFailure)
    {
        try { await operation(); }
        catch
        {
            try { observeFailure(); }
            catch { /* Best-effort metadata must preserve the original failure. */ }
            throw;
        }
    }

    internal static string SafePath(Uri origin, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var target)) return "invalid";
        if (target.Scheme != origin.Scheme || target.Host != origin.Host || target.Port != origin.Port
            || target.UserInfo.Length != 0) return "cross-origin";
        if (target.AbsolutePath.Equals("/contact", StringComparison.OrdinalIgnoreCase)) return "contact";
        if (target.AbsolutePath.Equals("/legal", StringComparison.OrdinalIgnoreCase)) return "legal";
        if (target.AbsolutePath.Equals("/legal/nondisclosureagreement", StringComparison.OrdinalIgnoreCase)) return "nda";
        return "same-origin-other";
    }

    private void Record(string stage, string url, int? status = null)
    {
        lock (gate)
        {
            if (disposed || observed >= 32) return;
            observed++;
            events.Enqueue(new
            {
                stage,
                path = SafePath(origin, url),
                elapsedMs = Math.Min(60000, elapsed.ElapsedMilliseconds),
                status = status is >= 100 and <= 599 ? status : null
            });
        }
    }

    private void Started(object? sender, IRequest request)
    {
        if (request.ResourceType == "document") Record("request", request.Url);
    }
    private void Responded(object? sender, IResponse response)
    {
        if (response.Request.ResourceType == "document") Record("response", response.Url, response.Status);
    }
    private void Finished(object? sender, IRequest request)
    {
        if (request.ResourceType == "document") Record("finished", request.Url);
    }
    private void Failed(object? sender, IRequest request)
    {
        if (request.ResourceType == "document") Record("failed", request.Url);
    }
    private void Navigated(object? sender, IFrame frame)
    {
        if (frame == page.MainFrame) Record("committed", frame.Url);
    }
    internal void WriteFailure(ITestOutputHelper output, string culture) =>
        output.WriteLine("WEB_CONTACT_FALLBACK_NAVIGATION_FAILURE " + JsonSerializer.Serialize(new
        {
            culture = culture == "th" ? "th" : "en",
            pageClosed = page.IsClosed,
            finalPath = page.IsClosed ? "closed" : SafePath(origin, page.Url),
            finalContactFragment = !page.IsClosed && Uri.TryCreate(page.Url, UriKind.Absolute, out var location)
                && location.Fragment == "#contact-us",
            events = events.ToArray(),
        }));

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        Detach(() => page.Request -= Started);
        Detach(() => page.Response -= Responded);
        Detach(() => page.RequestFinished -= Finished);
        Detach(() => page.RequestFailed -= Failed);
        Detach(() => page.FrameNavigated -= Navigated);
    }

    private void Detach(Action remove)
    {
        try { remove(); }
        catch { DetachFailures++; /* Keep attempting other removals and preserve the original test failure. */ }
    }
}
