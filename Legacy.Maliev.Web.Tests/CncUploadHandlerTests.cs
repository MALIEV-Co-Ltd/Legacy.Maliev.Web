using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncUploadHandlerTests
{
    [Fact]
    public async Task CancellationBeforeTransportEntry_ReleasesReservationWithoutCleanup()
    {
        var fixture = new Fixture();
        var transport = new CancelBeforeSendTransport();
        var handler = new CncUploadHandler(fixture.Bindings, new(fixture.Provider, fixture.Clock), transport,
            fixture.Antiforgery, fixture.Environment, new ConfigurationBuilder().Build(), fixture.Clock,
            NullLogger<CncUploadHandler>.Instance, fixture.Store);
        var context = fixture.Request();
        await (await handler.HandleAsync(context)).ExecuteAsync(context);
        Assert.Equal(0, transport.Deletes);
        Assert.True(fixture.Bindings.TryValidateForm(fixture.FormToken, fixture.Session, out var form));
        Assert.True(fixture.Store.TryReserve(new(form!.FormId, fixture.Session, "item", "model", "retry",
            fixture.Clock.GetUtcNow().AddHours(3)), fixture.Clock.GetUtcNow(), 40, out _));
    }

    [Fact]
    public async Task Upload_ReservesBeforeSendAndFinalizesExactReceipt()
    {
        var fixture = new Fixture();
        var context = fixture.Request();
        var json = await fixture.Execute(context);
        Assert.True(json.GetProperty("success").GetBoolean());
        var path = json.GetProperty("path").GetString()!;
        Assert.StartsWith($"2026-9-6/{fixture.Session}/", path);
        Assert.EndsWith(".step", path);
        Assert.Equal(path, fixture.Transport.Path);
        var receipt = json.GetProperty("receipt").GetString()!;
        Assert.True(fixture.Bindings.TryValidateForm(fixture.FormToken, fixture.Session, out var form));
        Assert.True(fixture.Bindings.TryValidateReceipt(receipt, "item", "model", "original.step", path,
            fixture.Session, form!.FormId, [], out _));
        Assert.True(fixture.Store.TryClaimAll([new(form.FormId, fixture.Session, "item", "model", receipt)], fixture.Clock.GetUtcNow(), out _));
        Assert.Equal(1, fixture.Antiforgery.Calls);
        Assert.Equal(1, fixture.Transport.Uploads);
    }

    [Theory]
    [InlineData(CncUploadTransportOutcome.NotSent, false, false)]
    [InlineData(CncUploadTransportOutcome.Rejected, false, false)]
    [InlineData(CncUploadTransportOutcome.Unknown, true, false)]
    [InlineData(CncUploadTransportOutcome.Unknown, false, true)]
    public async Task Failure_ReleasesOnlyDefinitiveFailureOrConfirmedCleanup(CncUploadTransportOutcome outcome, bool cleanup, bool locked)
    {
        var fixture = new Fixture();
        fixture.Transport.Outcome = outcome;
        fixture.Transport.Cleanup = cleanup;
        var json = await fixture.Execute(fixture.Request());
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(outcome == CncUploadTransportOutcome.Unknown ? 1 : 0, fixture.Transport.Deletes);
        Assert.True(fixture.Bindings.TryValidateForm(fixture.FormToken, fixture.Session, out var form));
        Assert.Equal(!locked, fixture.Store.TryReserve(new(form!.FormId, fixture.Session, "item", "model", "next",
            fixture.Clock.GetUtcNow().AddHours(3)), fixture.Clock.GetUtcNow(), 40, out _));
        if (locked) Assert.Contains("remains locked", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Rejection_PreservesSourceStatusMessage()
    {
        var fixture = new Fixture();
        fixture.Transport.Outcome = CncUploadTransportOutcome.Rejected;
        fixture.Transport.Status = HttpStatusCode.UnsupportedMediaType;
        var json = await fixture.Execute(fixture.Request());
        Assert.Equal("The file could not be uploaded (UnsupportedMediaType). Please try again.", json.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("role")]
    [InlineData("duplicateRole")]
    [InlineData("duplicateForm")]
    [InlineData("wrongSession")]
    [InlineData("tamperedForm")]
    [InlineData("item")]
    [InlineData("noAdmission")]
    [InlineData("signature")]
    public async Task InvalidBindingOrAdmission_NeverSends(string failure)
    {
        var fixture = new Fixture();
        var context = fixture.Request(failure);
        var json = await fixture.Execute(context);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, fixture.Transport.Uploads);
    }

    [Fact]
    public async Task AntiforgeryFailure_Is400AndNeverSends()
    {
        var fixture = new Fixture();
        fixture.Antiforgery.Reject = true;
        var context = fixture.Request();
        await (await fixture.Handler.HandleAsync(context)).ExecuteAsync(context);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal(0, fixture.Transport.Uploads);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task ProductionWithoutDistributedReceipts_Is404()
    {
        var fixture = new Fixture();
        fixture.Environment.EnvironmentName = "Production";
        var context = fixture.Request();
        await (await fixture.Handler.HandleAsync(context)).ExecuteAsync(context);
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal(0, fixture.Transport.Uploads);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task MissingReceiptStore_Is404WithoutTransport(string environment)
    {
        var fixture = new Fixture();
        fixture.Environment.EnvironmentName = environment;
        var handler = new CncUploadHandler(fixture.Bindings, new(fixture.Provider, fixture.Clock), fixture.Transport,
            fixture.Antiforgery, fixture.Environment, new ConfigurationBuilder().Build(), fixture.Clock,
            NullLogger<CncUploadHandler>.Instance);
        var context = fixture.Request();
        await (await handler.HandleAsync(context)).ExecuteAsync(context);
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal(0, fixture.Transport.Uploads);
        Assert.Equal(0, fixture.Antiforgery.Calls);
    }

    [Fact]
    public async Task DuplicateFileFields_PreservesSourceSingularBindingToFirstFile()
    {
        var fixture = new Fixture();
        var context = fixture.Request();
        var original = context.Request.Form;
        var files = new FormFileCollection { original.Files[0], new FormFile(new MemoryStream([1]), 0, 1, "file", "ignored.exe") };
        context.Request.Form = new FormCollection(original.ToDictionary(entry => entry.Key, entry => entry.Value), files);
        var json = await fixture.Execute(context);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, fixture.Transport.Uploads);
    }

    [Theory]
    [InlineData("Maliev.Web.InstantQuotationSession.v1", true)]
    [InlineData("Maliev.Web.InstantQuotationSession.v2", false)]
    public void Session_PreservesProtectedIdentityAndMigratesLegacyPurpose(string purpose, bool rewritten)
    {
        var provider = new EphemeralDataProtectionProvider();
        var session = Guid.NewGuid().ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "iq_session=" + provider.CreateProtector(purpose).Protect(session);
        var helper = new CncQuotationSession(provider, new Clock());
        Assert.Equal(session, helper.GetOrCreate(context));
        Assert.Equal(session, helper.GetOrCreate(context));
        Assert.Equal(rewritten, context.Response.Headers.ContainsKey("Set-Cookie"));
        if (rewritten)
        {
            var cookie = context.Response.Headers.SetCookie.ToString();
            Assert.Contains("secure", cookie);
            Assert.Contains("httponly", cookie);
            Assert.Contains("samesite=lax", cookie);
        }
    }

    private sealed class Fixture
    {
        internal readonly Clock Clock = new();
        internal readonly EphemeralDataProtectionProvider Provider = new();
        internal readonly InMemoryCncUploadReceiptStore Store = new();
        internal readonly Transport Transport = new();
        internal readonly Antiforgery Antiforgery = new();
        internal readonly Environment Environment = new();
        internal readonly string Session = Guid.NewGuid().ToString();
        internal readonly CncProtectedUploadBindings Bindings;
        internal readonly string FormToken;
        internal readonly CncUploadHandler Handler;

        internal Fixture()
        {
            Bindings = new(Provider, Clock);
            FormToken = Bindings.CreateFormToken(Session);
            Handler = new(Bindings, new(Provider, Clock), Transport, Antiforgery, Environment,
                new ConfigurationBuilder().Build(), Clock, NullLogger<CncUploadHandler>.Instance, Store);
            Transport.OnUpload = () =>
            {
                Assert.True(Bindings.TryValidateForm(FormToken, Session, out var form));
                Assert.False(Store.TryReserve(new(form!.FormId, Session, "item", "model", "duplicate",
                    Clock.GetUtcNow().AddHours(3)), Clock.GetUtcNow(), 40, out _));
            };
        }

        internal HttpContext Request(string? failure = null)
        {
            var context = new DefaultHttpContext();
            context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
            context.Response.Body = new MemoryStream();
            context.Request.Method = "POST";
            context.Request.Path = "/InstantQuotation/CNC-Machining";
            context.Request.QueryString = new("?handler=UploadFile&uploadRole=model");
            context.Request.ContentType = "multipart/form-data; boundary=test";
            context.Request.Headers.Cookie = "iq_session=" + Provider.CreateProtector("Maliev.Web.InstantQuotationSession.v2")
                .Protect(failure == "wrongSession" ? Guid.NewGuid().ToString() : Session);
            var data = failure == "signature" ? Encoding.UTF8.GetBytes("not a CAD file")
                : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Cnc", "box-20x30x40.step"));
            var file = new FormFile(new MemoryStream(data), 0, data.Length, "file", "original.step")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/octet-stream",
            };
            context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["uploadRole"] = failure == "duplicateRole" ? new StringValues(["model", "model"]) : failure == "role" ? "drawing" : "model",
                ["quotationFormToken"] = failure == "duplicateForm" ? new StringValues([FormToken, FormToken]) : failure == "tamperedForm" ? "tampered" : FormToken,
                ["itemId"] = failure == "item" ? "../item" : "item",
                ["sessionId"] = "untrusted-browser-value",
            }, new FormFileCollection { file });
            if (failure != "noAdmission") CncUploadAdmissionPolicy.SetValidatedRole(context, "model");
            return context;
        }

        internal async Task<JsonElement> Execute(HttpContext context)
        {
            await (await Handler.HandleAsync(context)).ExecuteAsync(context);
            context.Response.Body.Position = 0;
            using var json = await JsonDocument.ParseAsync(context.Response.Body);
            return json.RootElement.Clone();
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class CancelBeforeSendTransport : ICncFileTransport
    {
        internal int Deletes;
        public Task<CncUploadTransportResult> UploadAsync(string path, byte[] data, string type, CancellationToken token)
            // Neither dependency may be reached when cancellation precedes transport entry.
            => new CncFileTransport(null!, null!).UploadAsync(path, data, type, new CancellationToken(true));
        public Task<bool> DeleteReservedObjectAsync(string path, CancellationToken token)
        {
            Deletes++;
            return Task.FromResult(false);
        }
    }

    private sealed class Transport : ICncFileTransport
    {
        internal Action? OnUpload;
        internal CncUploadTransportOutcome Outcome = CncUploadTransportOutcome.Uploaded;
        internal HttpStatusCode? Status;
        internal bool Cleanup;
        internal int Uploads;
        internal int Deletes;
        internal string? Path;
        public Task<CncUploadTransportResult> UploadAsync(string reservedObjectPath, byte[] data, string contentType, CancellationToken cancellationToken)
        {
            OnUpload?.Invoke();
            Uploads++;
            Path = reservedObjectPath;
            return Task.FromResult(new CncUploadTransportResult(Outcome, Status));
        }
        public Task<bool> DeleteReservedObjectAsync(string reservedObjectPath, CancellationToken cancellationToken)
        {
            Assert.Equal(Path, reservedObjectPath);
            Deletes++;
            return Task.FromResult(Cleanup);
        }
    }

    private sealed class Antiforgery : IAntiforgery
    {
        internal bool Reject;
        internal int Calls;
        public Task ValidateRequestAsync(HttpContext httpContext)
        {
            Calls++;
            if (Reject) throw new AntiforgeryValidationException("Invalid token");
            return Task.CompletedTask;
        }
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => throw new NotSupportedException();
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => throw new NotSupportedException();
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(!Reject);
        public void SetCookieTokenAndHeader(HttpContext httpContext) => throw new NotSupportedException();
    }

    private sealed class Environment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
