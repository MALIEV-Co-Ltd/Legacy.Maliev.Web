using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncSignedLinkClientTests
{
    private const string ObjectName = "instant-quotation/2026-9-7/11111111-1111-4111-8111-111111111111/22222222222242228222222222222222.step";

    [Fact]
    public async Task GetAsync_UsesAuthenticatedExistingFileServiceContract()
    {
        var handler = new RecordingHandler(_ => Json("\"https://storage.googleapis.test/object?signature=opaque\""));
        var tokens = new RecordingTokenProvider("service-token");
        var client = CreateClient(handler, tokens);

        Uri? result = await client.GetAsync(ObjectName, default);

        Assert.Equal("https://storage.googleapis.test/object?signature=opaque", result?.AbsoluteUri);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer service-token", request.Authorization);
        Assert.Contains("uploads/signedurl?", request.Path, StringComparison.Ordinal);
        Assert.Contains("bucket=maliev-quotation-requests", request.Path, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(ObjectName), request.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(tokens.Invalidated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("instant-quotation/2026-9-7/not-a-session/22222222222242228222222222222222.step")]
    [InlineData("instant-quotation/2026-09-07/11111111-1111-4111-8111-111111111111/22222222222242228222222222222222.step")]
    [InlineData("instant-quotation/2026-9-7/11111111-1111-4111-8111-111111111111/../../private.step")]
    [InlineData("instant-quotation/2026-9-7/11111111-1111-4111-8111-111111111111/22222222222242228222222222222222.exe")]
    public async Task GetAsync_RejectsNonFinalizedCoordinatesBeforeTransport(string objectName)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not send"));
        var client = CreateClient(handler, new RecordingTokenProvider("service-token"));

        Assert.Null(await client.GetAsync(objectName, default));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("\"http://storage.test/object\"")]
    [InlineData("\"https://user:secret@storage.test/object\"")]
    [InlineData("\"/relative/object\"")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task GetAsync_RejectsUnsafeOrMalformedResponse(string body)
    {
        var client = CreateClient(new RecordingHandler(_ => Json(body)), new RecordingTokenProvider("service-token"));

        Assert.Null(await client.GetAsync(ObjectName, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task GetAsync_FailsClosedAndInvalidatesRejectedToken(HttpStatusCode status, bool invalidates)
    {
        var tokens = new RecordingTokenProvider("service-token");
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(status)), tokens);

        Assert.Null(await client.GetAsync(ObjectName, default));
        Assert.Equal(invalidates, tokens.Invalidated.Count == 1);
    }

    [Fact]
    public async Task GetAsync_RequiresTokenAndHonorsPreCancellation()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not send"));
        var noToken = CreateClient(handler, new RecordingTokenProvider(null));
        Assert.Null(await noToken.GetAsync(ObjectName, default));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = CreateClient(handler, new RecordingTokenProvider("service-token"));
        Assert.Null(await cancelled.GetAsync(ObjectName, cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AcquiredRejectedResponse_InvalidatesTokenBeforeHonoringCallerCancellation(HttpStatusCode status)
    {
        using var caller = new CancellationTokenSource();
        var tokens = new RecordingTokenProvider("service-token");
        var handler = new RecordingHandler(_ =>
        {
            caller.Cancel();
            return new HttpResponseMessage(status);
        });
        var client = CreateClient(handler, tokens);

        Assert.Null(await client.GetAsync(ObjectName, caller.Token));
        Assert.Equal("service-token", Assert.Single(tokens.Invalidated));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ResponseDisposalTransportFault_PreservesNullResult()
    {
        var content = new ThrowingDisposeContent();
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var tokens = new RecordingTokenProvider("service-token");
        var client = CreateClient(handler, tokens);

        Assert.Null(await client.GetAsync(ObjectName, default));
        Assert.Equal(1, content.Disposals);
        Assert.Single(handler.Requests);
        Assert.Empty(tokens.Invalidated);
    }

    private sealed class ThrowingDisposeContent : HttpContent
    {
        private static readonly byte[] Body = Encoding.UTF8.GetBytes("\"https://storage.googleapis.test/object\"");
        internal int Disposals { get; private set; }
        protected override bool TryComputeLength(out long length) { length = Body.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(Body, 0, Body.Length);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Disposals++;
                throw new IOException("Simulated response disposal failure.");
            }
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task BodyExpiry_UsesConfiguredFiniteTimeoutWithoutLateLink(bool cooperative, bool normalRegistration)
    {
        var content = new GatedContent(cooperative, "\"https://storage.googleapis.test/late\"");
        using var harness = new DeadlineHarness(content, TimeSpan.FromMilliseconds(500), normalRegistration);
        Task<Uri?> operation = harness.Client.GetAsync(ObjectName, default);
        try
        {
            await BoundaryAwait(content.Entered.Task, "body-entry");
            Assert.Null(await BoundaryAwait(operation, "finite-body-null-before-release"));
            await BoundaryAwait(content.Cancelled.Task, "finite-body-cancellation-forwarded");
            Assert.False(content.Released.Task.IsCompleted);
            Assert.Single(harness.Handler.Requests);
            Assert.Empty(harness.Tokens.Invalidated);
            if (!cooperative)
            {
                // Do not dispose an HttpContent before its pending buffer finishes:
                // a buffer created after an idempotent Dispose can otherwise escape cleanup.
                Assert.False(content.Disposed.Task.IsCompleted);
            }
        }
        finally
        {
            await ReleaseAndDrain(operation, content);
        }
        Assert.Null(await operation);
        Assert.True(content.Disposed.Task.IsCompleted);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task CallerCancellation_ReturnsNullIncludingIntentionallyInfiniteTimeout(bool cooperative, bool infinite)
    {
        var content = new GatedContent(cooperative, "\"https://storage.googleapis.test/late\"");
        using var harness = new DeadlineHarness(content,
            infinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30), true);
        using var caller = new CancellationTokenSource();
        Task<Uri?> operation = harness.Client.GetAsync(ObjectName, caller.Token);
        try
        {
            await BoundaryAwait(content.Entered.Task, "caller-body-entry");
            caller.Cancel();
            Assert.Null(await BoundaryAwait(operation, "caller-null-before-release"));
            await BoundaryAwait(content.Cancelled.Task, "caller-body-cancellation-forwarded");
            Assert.False(content.Released.Task.IsCompleted);
            if (!cooperative) Assert.False(content.Disposed.Task.IsCompleted);
            Assert.Empty(harness.Tokens.Invalidated);
        }
        finally
        {
            await ReleaseAndDrain(operation, content);
        }
        Assert.Null(await operation);
        Assert.True(content.Disposed.Task.IsCompleted);
    }

    [Fact]
    public async Task HealthyGatedBody_PreservesAuthenticatedUriContract()
    {
        var content = new GatedContent(true, "\"https://storage.googleapis.test/object?signature=opaque\"");
        using var harness = new DeadlineHarness(content, TimeSpan.FromSeconds(30), true);
        Task<Uri?> operation = harness.Client.GetAsync(ObjectName, default);
        try
        {
            await BoundaryAwait(content.Entered.Task, "healthy-body-entry");
            content.Released.TrySetResult(true);
            var result = await BoundaryAwait(operation, "healthy-body-result");
            Assert.Equal("https://storage.googleapis.test/object?signature=opaque", result?.AbsoluteUri);
            var request = Assert.Single(harness.Handler.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer service-token", request.Authorization);
            Assert.Contains("bucket=maliev-quotation-requests", request.Path, StringComparison.Ordinal);
            Assert.Contains(Uri.EscapeDataString(ObjectName), request.Path, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(harness.Tokens.Invalidated);
        }
        finally { await ReleaseAndDrain(operation, content); }
        Assert.True(content.Disposed.Task.IsCompleted);
    }

    [Fact]
    public async Task UnknownLengthBody_StillHonors65536ByteCap()
    {
        var content = new GatedContent(true, "\"" + new string('x', 65_535) + "\"");
        using var harness = new DeadlineHarness(content, TimeSpan.FromSeconds(30), false);
        Task<Uri?> operation = harness.Client.GetAsync(ObjectName, default);
        try
        {
            await BoundaryAwait(content.Entered.Task, "cap-body-entry");
            Assert.Null(content.Headers.ContentLength);
            content.Released.TrySetResult(true);
            Assert.Null(await BoundaryAwait(operation, "cap-null"));
            Assert.Empty(harness.Tokens.Invalidated);
        }
        finally { await ReleaseAndDrain(operation, content); }
        Assert.True(content.Disposed.Task.IsCompleted);
    }

    [Fact]
    public void NormalFilesRegistration_PreservesResilienceAndSeparateTenSecondOperationBudget()
    {
        var content = new GatedContent(true, "\"https://storage.googleapis.test/object\"");
        using var harness = new DeadlineHarness(content, null, true);
        Assert.Equal(Timeout.InfiniteTimeSpan, harness.Http.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(10), harness.OperationTimeout);
        Assert.Empty(harness.Handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealDeadlineClient_UnavailableLinkPreventsAllNotificationsIncludingLateRelease(bool cooperative)
    {
        var content = new GatedContent(cooperative, "\"https://storage.googleapis.test/late\"");
        using var harness = new DeadlineHarness(content, TimeSpan.FromMilliseconds(500), true);
        var notifications = new UnexpectedNotifications();
        var coordinator = new CncNotificationCoordinator(harness.Client, notifications);
        var submission = new CncSubmission
        {
            OrderItems = [new ItemDetail { FileName = "part.step", StoragePath = "owned/part.step" }]
        };
        var operation = coordinator.DeliverAsync(submission, 42, [new(ObjectName)],
            Guid.NewGuid(), default);
        try
        {
            await BoundaryAwait(content.Entered.Task, "coordinator-body-entry");
            var result = await BoundaryAwait(operation, "coordinator-null-before-release");
            Assert.False(result.LinksResolved);
            Assert.False(result.Composed);
            Assert.Null(result.Customer);
            Assert.Null(result.Manufacturing);
            Assert.Equal(0, notifications.Calls);
            Assert.False(content.Released.Task.IsCompleted);
        }
        finally { await ReleaseAndDrain(operation, content); }
        Assert.Equal(0, notifications.Calls);
        Assert.True(content.Disposed.Task.IsCompleted);
    }

    [Fact]
    public async Task HeadersAndBody_ShareOneConfiguredDeadlineWithoutRestart()
    {
        var content = new GatedContent(true, "\"https://storage.googleapis.test/late\"");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var handler = new GatedSendHandler(content, true);
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://files.test/"),
            Timeout = TimeSpan.FromSeconds(4)
        };
        var client = CreateClient(new BoundaryFactory(http), new RecordingTokenProvider("service-token"));
        Task<Uri?> operation = client.GetAsync(ObjectName, default);
        try
        {
            await BoundaryAwait(handler.Entered.Task, "cumulative-send-entry");
            // Deliberate behavior timer: use roughly half of the one 4s budget
            // before headers. This is not a startup wait or timing optimization.
            await Task.Delay(TimeSpan.FromSeconds(2));
            handler.Released.TrySetResult(true);
            await BoundaryAwait(content.Entered.Task, "cumulative-body-entry");
            // Shared budget has roughly 2s left; resetting at headers grants 4s.
            // A 3s independent assertion window separates those cases broadly.
            Assert.Null(await BoundaryAwait(operation, "single-send-body-budget",
                TimeSpan.FromSeconds(3)));
            Assert.False(content.Released.Task.IsCompleted);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            handler.Released.TrySetResult(true);
            await ReleaseAndDrain(operation, content);
            await BoundaryAwait(handler.Finished.Task, "cumulative-send-drain");
        }
        Assert.Null(await operation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequestDeadline_ClosesHeldHeadersAndDisposesLateResponseWithoutReading(bool cooperative)
    {
        var content = new GatedContent(true, "\"https://storage.googleapis.test/late\"");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        // If old source eventually reads the late response during cleanup,
        // permit it to finish; the primary held-header watchdog still fails.
        content.Released.TrySetResult(true);
        using var handler = new GatedSendHandler(content, cooperative);
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://files.test/"),
            Timeout = TimeSpan.FromMilliseconds(500)
        };
        var tokens = new RecordingTokenProvider("service-token");
        var client = CreateClient(new BoundaryFactory(http), tokens);
        Task<Uri?> operation = client.GetAsync(ObjectName, default);
        try
        {
            await BoundaryAwait(handler.Entered.Task, "held-header-entry");
            Assert.Null(await BoundaryAwait(operation, "request-null-before-header-release"));
            Assert.False(handler.Released.Task.IsCompleted);
            Assert.False(content.Entered.Task.IsCompleted);
            Assert.Empty(tokens.Invalidated);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            handler.Released.TrySetResult(true);
            using var watchdog = new CancellationTokenSource();
            var guard = Task.Delay(TimeSpan.FromSeconds(5), watchdog.Token);
            Assert.True(ReferenceEquals(await Task.WhenAny(operation, guard), operation),
                "[SIGNED_LINK_CLEANUP] Held-header operation did not drain after release.");
            try { await operation; } catch { /* Observe after primary boundary assertion. */ }
            await BoundaryAwait(handler.Finished.Task, "held-header-handler-drain");
            // Cooperative handler disposes its unsent response on cancellation.
            // Ignored cancellation returns a real late response whose ownership
            // must transfer to the client's observed completion/disposal path.
            await BoundaryAwait(content.Disposed.Task, "held-header-response-disposal");
            watchdog.Cancel();
        }
        Assert.Null(await operation);
        Assert.False(content.Entered.Task.IsCompleted);
    }

    private sealed class GatedSendHandler(GatedContent content, bool cooperative) : HttpMessageHandler
    {
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            Entered.TrySetResult(true);
            try
            {
                if (cooperative) await Released.Task.WaitAsync(cancellationToken);
                else await Released.Task;
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
            finally { Finished.TrySetResult(true); }
        }
    }

    private static async Task<T> BoundaryAwait<T>(Task<T> operation, string phase, TimeSpan? assertionWindow = null)
    {
        using var watchdog = new CancellationTokenSource();
        var guard = Task.Delay(assertionWindow ?? TimeSpan.FromSeconds(5), watchdog.Token);
        Task completed = await Task.WhenAny(operation, guard);
        Assert.True(ReferenceEquals(completed, operation),
            "[SIGNED_LINK_DEADLINE] " + phase + ": operation did not complete before the test watchdog; gate remains held.");
        watchdog.Cancel();
        return await operation;
    }

    private static async Task ReleaseAndDrain(Task operation, GatedContent content)
    {
        content.Released.TrySetResult(true);
        using var watchdog = new CancellationTokenSource();
        var guard = Task.Delay(TimeSpan.FromSeconds(5), watchdog.Token);
        Assert.True(ReferenceEquals(await Task.WhenAny(operation, guard), operation),
            "[SIGNED_LINK_CLEANUP] Operation did not drain after release.");
        try { await operation; } catch { /* Observe cleanup fault without replacing primary assertion. */ }
        await BoundaryAwait(content.Finished.Task, "serializer-drain");
        await BoundaryAwait(content.Disposed.Task, "eventual-content-disposal");
        watchdog.Cancel();
    }

    private sealed class GatedContent(bool cooperative, string body) : HttpContent
    {
        internal TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult(true));
            Entered.TrySetResult(true);
            try
            {
                if (cooperative) await Released.Task.WaitAsync(cancellationToken);
                else await Released.Task;
                await stream.WriteAsync(Encoding.UTF8.GetBytes(body),
                    cooperative ? cancellationToken : CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cooperative WaitAsync can unregister the earlier diagnostic callback
                // while unwinding. Observe cancellation consumed by the serializer itself.
                Cancelled.TrySetResult(true);
                throw;
            }
            finally { Finished.TrySetResult(true); }
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) Disposed.TrySetResult(true);
        }
    }

    private sealed class BoundaryFactory(HttpClient client) : IHttpClientFactory
    {
        internal TimeSpan Timeout => client.Timeout;
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("files", name);
            return client;
        }
    }

    private sealed class DeadlineHarness : IDisposable
    {
        private readonly ServiceProvider? services;
        internal RecordingHandler Handler { get; }
        internal RecordingTokenProvider Tokens { get; } = new("service-token");
        internal HttpClient Http { get; }
        internal ICncSignedLinkClient Client { get; }
        internal TimeSpan OperationTimeout { get; }

        internal DeadlineHarness(GatedContent content, TimeSpan? timeout, bool normalRegistration)
        {
            OperationTimeout = timeout ?? TimeSpan.FromSeconds(10);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            Handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            if (normalRegistration)
            {
                var collection = new ServiceCollection();
                collection.AddLogging();
                collection.AddLegacyServiceClients(new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Services:File"] = "https://files.test/" }).Build());
                collection.AddSingleton<IServiceAccessTokenProvider>(Tokens);
                collection.AddHttpClient("files").ConfigurePrimaryHttpMessageHandler(() => Handler);
                if (timeout is not null) collection.Configure<CncSignedLinkDeadlineOptions>(options => options.Timeout = timeout.Value);
                services = collection.BuildServiceProvider();
                OperationTimeout = services.GetRequiredService<IOptions<CncSignedLinkDeadlineOptions>>().Value.Timeout;
                Http = services.GetRequiredService<IHttpClientFactory>().CreateClient("files");
                Client = services.GetRequiredService<ICncSignedLinkClient>();
            }
            else
            {
                Http = new HttpClient(Handler, disposeHandler: false)
                {
                    BaseAddress = new Uri("https://files.test/"),
                    Timeout = timeout ?? TimeSpan.FromSeconds(10)
                };
                Client = CreateClient(new BoundaryFactory(Http), Tokens);
            }
        }
        public void Dispose() { Http.Dispose(); services?.Dispose(); Handler.Dispose(); }
    }

    private static ICncSignedLinkClient CreateClient(IHttpClientFactory factory, IServiceAccessTokenProvider tokens)
    {
        var constructor = typeof(CncSignedLinkClient).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var timeout = factory is BoundaryFactory boundary ? boundary.Timeout : TimeSpan.FromSeconds(10);
        return (ICncSignedLinkClient)constructor.Invoke([factory, tokens, Options.Create(new CncSignedLinkDeadlineOptions { Timeout = timeout })]);
    }

    private sealed class UnexpectedNotifications : INotificationClient
    {
        internal int Calls { get; private set; }
        public Task<NotificationResult> SendAsync(NotificationChannel channel, EmailNotification notification,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Signed-link unavailable must not send a notification.");
        }
        public Task<NotificationResult> SendIdempotentAsync(NotificationChannel channel, EmailNotification notification,
            Guid operationId, CancellationToken cancellationToken) => SendAsync(channel, notification, cancellationToken);
    }

    private static ICncSignedLinkClient CreateClient(HttpMessageHandler handler, IServiceAccessTokenProvider tokens)
    {
        return CreateClient(new Factory(handler), tokens);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("files", name);
            return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://files.test/") };
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty));
            return Task.FromResult(respond(request));
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Authorization);

    private sealed class RecordingTokenProvider(string? token) : IServiceAccessTokenProvider
    {
        public List<string> Invalidated { get; } = [];

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(token);

        public void Invalidate(string value) => Invalidated.Add(value);
    }
}
