using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual page/circuit physical-failure presentation over controlled bytes; no FileService or financial execution.</summary>
public sealed class InstantQuotationTerminalPhysicalFailureBrowserTests
{
    [Theory]
    [InlineData("en", 375, "open", "GeometryReview", "This mesh has open or non-manifold surfaces. Contact our engineers for a file review.")]
    [InlineData("th", 320, "open", "GeometryReview", "ไฟล์เมชมีพื้นผิวเปิดหรือโครงสร้างไม่สมบูรณ์ กรุณาติดต่อวิศวกรเพื่อช่วยตรวจสอบไฟล์")]
    [InlineData("en", 375, "layers", "ComplexityExceeded", "This file exceeds the analysis limits. Simplify the mesh or contact our engineers for a quotation.")]
    [InlineData("th", 320, "layers", "ComplexityExceeded", "ไฟล์นี้เกินขีดจำกัดของการวิเคราะห์ กรุณาลดความซับซ้อนของเมชหรือติดต่อวิศวกรเพื่อขอใบเสนอราคา")]
    [InlineData("en", 375, "malformed", "SimulationUnavailable", "Print simulation is unavailable for this file. Contact our engineers for a quotation.")]
    [InlineData("th", 320, "malformed", "SimulationUnavailable", "ไม่สามารถจำลองการพิมพ์สำหรับไฟล์นี้ได้ กรุณาติดต่อวิศวกรเพื่อขอใบเสนอราคา")]
    public async Task ActualTerminalFailureEndsPendingStatusWithoutAuthorizingReview(
        string culture, int width, string input, string failure, string expected)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width, input);
        var panel = fixture.Page.Locator("[data-workflow-physical-analysis]");
        var check = panel.Locator("button");
        await Assertions.Expect(check).ToBeEnabledAsync();
        await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
        Assert.Null((await fixture.StoredAsync()).QuoteAuthorization);
        await check.ClickAsync();
        try
        {
            await fixture.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(check).ToBeDisabledAsync();
            await Assertions.Expect(panel.Locator("[role='status']")).ToHaveTextAsync(
                culture == "th" ? "กำลังตรวจสอบการพิมพ์จริง…" : "Checking physical build…");
            await Assertions.Expect(panel.Locator("[data-physical-analysis-result]")).ToHaveCountAsync(0);
            await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
            Assert.Null((await fixture.StoredAsync()).QuoteAuthorization);
        }
        finally { fixture.Reader.Release.TrySetResult(); }

        var result = panel.Locator("[data-physical-analysis-result='unavailable']");
        await Assertions.Expect(result).ToHaveAttributeAsync("data-physical-analysis-failure", failure);
        await Assertions.Expect(result).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(result).ToHaveTextAsync(expected);
        await Assertions.Expect(check).ToBeEnabledAsync();
        await Assertions.Expect(panel.Locator("[role='status']")).ToHaveCountAsync(1);
        await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
        Assert.Null((await fixture.StoredAsync()).QuoteAuthorization);
        Assert.Equal(0, fixture.Store.AuthorizationWrites);
        Assert.Equal(1, fixture.Reader.Reads);
        Assert.Equal((fixture.SessionId, (string?)null, fixture.PartId), fixture.Reader.LastRequest);
        Assert.Equal(0, fixture.PageErrors);
    }

    // Return no quote on either overload. No pricing kernel, financial provider or ticket issuer is invoked.
    private sealed class NoQuotePricing : IInstantQuotationAuthoritativePricingService
    {
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<InstantQuotationOrderQuote?>(null); }
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken token) => QuoteAsync(session, owner, comparisons, token);
    }

    private sealed class AuthorityGuardStore(IInstantQuotationSessionStore inner) : IInstantQuotationSessionStore
    {
        private int authorizationWrites;
        public int AuthorizationWrites => Volatile.Read(ref authorizationWrites);
        public Task<InstantQuotationSessionState> CreateAsync(string? owner, InstantQuotationOrderState request,
            CancellationToken token) => inner.CreateAsync(owner, request, token);
        public Task<InstantQuotationSessionState?> GetAsync(string id, string? owner, CancellationToken token) =>
            inner.GetAsync(id, owner, token);
        public Task<bool> RemoveAsync(string id, string? owner, CancellationToken token) => inner.RemoveAsync(id, owner, token);
        public Task<bool> PutAsync(InstantQuotationSessionState state, string? owner, CancellationToken token)
        {
            if (state.QuoteAuthorization is not null)
            {
                Interlocked.Increment(ref authorizationWrites);
                throw new InvalidOperationException("Physical-failure presentation must not create authorization.");
            }
            return inner.PutAsync(state, owner, token);
        }
    }

    private sealed class ControlledReader(byte[] bytes) : IInstantQuotationPhysicalAnalysisInputReader
    {
        public Guid FileId { get; } = Guid.NewGuid();
        public string Digest { get; } = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        public int Length => bytes.Length;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;
        public int Reads => Volatile.Read(ref reads);
        public (string, string?, Guid)? LastRequest { get; private set; }
        public async Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(string id, string? owner,
            Guid part, CancellationToken token)
        {
            Interlocked.Increment(ref reads);
            LastRequest = (id, owner, part);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return new(bytes, InstantQuotationPhysicalAnalysisInputFailure.None, FileId, "controlled.stl", Digest);
        }
    }

    private sealed class Host(ControlledReader reader) : TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory())
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationAuthoritativePricingService>();
                services.AddSingleton<IInstantQuotationAuthoritativePricingService, NoQuotePricing>();
                services.RemoveAll<IInstantQuotationPhysicalAnalysisInputReader>();
                services.AddSingleton<IInstantQuotationPhysicalAnalysisInputReader>(reader);
                var original = services.Single(item => item.ServiceType == typeof(IInstantQuotationSessionStore));
                services.RemoveAll<IInstantQuotationSessionStore>();
                services.Add(new ServiceDescriptor(typeof(IInstantQuotationSessionStore), provider =>
                    new AuthorityGuardStore((IInstantQuotationSessionStore)(original.ImplementationInstance
                        ?? original.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!))), original.Lifetime));
            });
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ControlledReader Reader { get; }
        private Host Factory { get; }
        private IPlaywright? playwright;
        private IBrowser? browser;
        private IBrowserContext? context;
        public IPage Page { get; private set; } = null!;
        public string SessionId { get; private set; } = string.Empty;
        public Guid PartId { get; } = Guid.NewGuid();
        public AuthorityGuardStore Store { get; private set; } = null!;
        private int pageErrors;
        public int PageErrors => Volatile.Read(ref pageErrors);
        public ILocator Review => Page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        private Fixture(byte[] bytes) { Reader = new(bytes); Factory = new(Reader); }

        private static int ReserveFreePort()
        {
            // Keep test listeners outside the outbound client-port pool on Windows.
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var listener = new TcpListener(System.Net.IPAddress.Loopback, Random.Shared.Next(20000, 30000));
                try
                {
                    listener.Start();
                    return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                }
                catch (SocketException exception) when (exception.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
                {
                    // Another task or an excluded port range owns this candidate.
                }
                finally
                {
                    listener.Stop();
                }
            }

            throw new InvalidOperationException("No loopback browser-test port is available in the test-server range.");
        }

        public static async Task<Fixture> CreateAsync(string culture, int width, string input)
        {
            var bytes = input switch
            {
                // Retained row label: a single open sheet cannot supply a recoverable solid.
                "open" => IrrecoverableSheet(),
                "layers" => Box(1000, false),
                _ => Encoding.ASCII.GetBytes("solid incomplete\nfacet normal 0 0 1\n"),
            };
            var fixture = new Fixture(bytes);
            try
            {
                var port = ReserveFreePort();
                var origin = new Uri($"http://127.0.0.1:{port}");
                fixture.Factory.UseKestrel(port);
                using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
                fixture.Store = Assert.IsType<AuthorityGuardStore>(fixture.Factory.Services.GetRequiredService<IInstantQuotationSessionStore>());
                fixture.playwright = await Playwright.CreateAsync();
                fixture.browser = await fixture.playwright.Chromium.LaunchAsync(new() { Headless = true });
                fixture.context = await fixture.browser.NewContextAsync(new()
                { ViewportSize = new() { Width = width, Height = 800 } });
                fixture.Page = await fixture.context.NewPageAsync();
                fixture.Page.PageError += (_, _) => Interlocked.Increment(ref fixture.pageErrors);
                await fixture.Page.GotoAsync(new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString(),
                    new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                await fixture.Page.WaitForFunctionAsync(
                    "() => typeof document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId === 'number'");
                var consent = fixture.Page.Locator("#cookieConsent [data-consent-action='reject']");
                if (await consent.IsVisibleAsync()) await consent.ClickAsync();
                var cookie = Assert.Single(await fixture.context.CookiesAsync(), item => item.Name == InstantQuotationSessionIdentityCookie.CookieName);
                var http = new DefaultHttpContext();
                http.Request.Headers.Cookie = $"{cookie.Name}={cookie.Value}";
                fixture.SessionId = fixture.Factory.Services.GetRequiredService<InstantQuotationSessionIdentityCookie>().TryRead(http)!;
                Assert.False(string.IsNullOrEmpty(fixture.SessionId));
                var stored = await fixture.StoredAsync();
                var reader = fixture.Reader;
                var reference = new InstantQuotationUploadReference(reader.FileId.ToString("D"));
                var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("controlled-historical", reference, reader.Digest),
                    new InstantQuotationGeometryClaim(1, reader.Digest, 8, 8, 8, 512, 384,
                        Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
                        12, 1, true, false, false, 1))!;
                var part = new InstantQuotationPart(fixture.PartId, "controlled.stl", reference, geometry,
                    new InstantQuotationPartConfiguration("PLA", "Black", 1),
                    new InstantQuotationPhysicalAnalysisUpload(reader.FileId, "controlled.stl", "model/stl",
                        reader.Length, reader.Digest, "clean"));
                Assert.True(await fixture.Store.PutAsync(stored with { RequestState = new([part]) }, null, default));
                await fixture.Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                await fixture.Page.WaitForFunctionAsync(
                    "() => typeof document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId === 'number'");
                await Assertions.Expect(fixture.Page.Locator("[data-workflow-physical-analysis] button")).ToBeEnabledAsync();
                Assert.Equal(0, fixture.Reader.Reads);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task<InstantQuotationSessionState> StoredAsync() =>
            Assert.IsType<InstantQuotationSessionState>(await Store.GetAsync(SessionId, null, default));

        public async ValueTask DisposeAsync()
        {
            Reader.Release.TrySetResult();
            try { if (context is not null) await context.DisposeAsync(); }
            finally
            {
                try { if (browser is not null) await browser.DisposeAsync(); }
                finally
                {
                    try { playwright?.Dispose(); }
                    finally { await Factory.DisposeAsync(); }
                }
            }
        }
    }

    private static byte[] IrrecoverableSheet()
    {
        byte[] sheet = Box(8, false)[..134];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sheet.AsSpan(80, 4), 1);
        return sheet;
    }

    private static byte[] Box(float height, bool omitLastFace)
    {
        Vector3[] vertices = [new(0, 0, 0), new(8, 0, 0), new(8, 8, 0), new(0, 8, 0),
            new(0, 0, height), new(8, 0, height), new(8, 8, height), new(0, 8, height)];
        int[][] faces = [[0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4],
            [1, 2, 6], [1, 6, 5], [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7]];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]);
        writer.Write((uint)(faces.Length - (omitLastFace ? 1 : 0)));
        foreach (var face in faces.Take(faces.Length - (omitLastFace ? 1 : 0)))
        {
            writer.Write(new byte[12]);
            foreach (var index in face)
            { writer.Write(vertices[index].X); writer.Write(vertices[index].Y); writer.Write(vertices[index].Z); }
            writer.Write((ushort)0);
        }
        return stream.ToArray();
    }
}
