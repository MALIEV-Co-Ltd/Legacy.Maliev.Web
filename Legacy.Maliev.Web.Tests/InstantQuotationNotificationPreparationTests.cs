using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationNotificationPreparationTests
{
    [Fact]
    public async Task ExactFinalizedBytesAndAuthoritativeMoneyPrepareBothFrozenMessages()
    {
        using var boundary = new Boundary();
        var before = DateTimeOffset.UtcNow;
        var result = await boundary.PrepareAsync();
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.NotNull(result.Payload);
        Assert.Equal("customer@example.test", result.Payload.Customer.To);
        Assert.Equal("manufacturing@maliev.com", result.Payload.Manufacturing.To);
        Assert.Contains("bucket=private-fixture", Assert.Single(boundary.Paths), StringComparison.Ordinal);
        Assert.Contains("objectName=clean%2Ffixture%20part.stl", boundary.Paths[0], StringComparison.Ordinal);
        Assert.True(boundary.AuthorizationSeen);
        Assert.InRange(result.Payload.ExpiresAt, before.AddMinutes(44), DateTimeOffset.UtcNow.AddMinutes(45));
        Assert.Contains(boundary.Quote.FinalOrderPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " THB", result.Payload.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("frozen-file.stl", result.Payload.Customer.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(401, true, false)]
    [InlineData(403, true, false)]
    [InlineData(503, false, true)]
    public async Task FailedFileDependencyPreventsBothPayloads(int status, bool available, bool authorized)
    {
        using var boundary = new Boundary { Status = status };
        var result = await boundary.PrepareAsync();
        Assert.Null(result.Payload);
        Assert.Equal(available, result.ServiceAvailable);
        Assert.Equal(authorized, result.Authorized);
        Assert.Equal(!authorized, boundary.Tokens.Invalidated);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://files.example/part")]
    [InlineData("https://private@files.example/part")]
    public async Task UnsafeSignedLinkPreventsPayload(string link)
    {
        using var boundary = new Boundary { Link = link };
        Assert.Null((await boundary.PrepareAsync()).Payload);
    }

    [Fact]
    public async Task UnknownSigningDateDoesNotGuessLinkLifetime()
    {
        using var boundary = new Boundary { IncludeDate = false };
        Assert.Null((await boundary.PrepareAsync()).Payload);
    }

    [Fact]
    public async Task UnmatchedFinalizedBytesNeverRequestSignedLink()
    {
        using var boundary = new Boundary();
        var result = await boundary.PrepareAsync(boundary.File with { Sha256 = new string('b', 64) });
        Assert.Null(result.Payload);
        Assert.Empty(boundary.Paths);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutTransport()
    {
        using var boundary = new Boundary();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => boundary.PrepareAsync(cancellationToken: cancellation.Token));
        Assert.Empty(boundary.Paths);
    }

    [Fact]
    public async Task DelayedHeadersAreDisposedWhenTheyArriveAfterDeadline()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var content = new ControlledContent(false);
        using var boundary = new Boundary
        {
            Timeout = TimeSpan.FromMilliseconds(50),
            Content = content,
            Transport = async response => { await release.Task; return response; },
        };
        try
        {
            var result = await boundary.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(result.Payload);
            Assert.False(result.ServiceAvailable);
            release.TrySetResult();
            await content.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult();
            await content.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ContentIgnoringCancellationIsDisposedBeforeItsWriterFinishes()
    {
        using var content = new ControlledContent(true);
        using var boundary = new Boundary { Timeout = TimeSpan.FromMilliseconds(50), Content = content };
        try
        {
            var result = await boundary.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(result.Payload);
            Assert.False(result.ServiceAvailable);
            await content.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(content.Finished.Task.IsCompleted);
        }
        finally
        {
            content.Release.TrySetResult();
            await content.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task OversizedResponseIsRejectedAndDisposed()
    {
        using var content = new ControlledContent(false,
            System.Text.Json.JsonSerializer.Serialize("https://files.example/part") + new string(' ', 65_537));
        using var boundary = new Boundary { Content = content };
        Assert.Null((await boundary.PrepareAsync()).Payload);
        await content.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ControlledContent : HttpContent
    {
        private readonly bool blocked;
        private readonly byte[] bytes;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ControlledContent(bool blocked, string value = "\"https://files.example/part\"")
        {
            this.blocked = blocked;
            bytes = System.Text.Encoding.UTF8.GetBytes(value);
            Headers.ContentType = new("application/json");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            try
            {
                if (blocked) await Release.Task;
                await stream.WriteAsync(bytes);
            }
            finally { Finished.TrySetResult(); }
        }

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Disposed.TrySetResult();
        }
    }

    private sealed class Boundary : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient http;
        public int Status { get; init; } = 200;
        public string Link { get; init; } = "https://files.example/part?one=a&two=b";
        public bool IncludeDate { get; init; } = true;
        public Func<HttpResponseMessage, Task<HttpResponseMessage>>? Transport { get; init; }
        public HttpContent? Content { get; init; }
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
        public bool AuthorizationSeen { get; private set; }
        public List<string> Paths { get; } = [];
        public Tokens Tokens { get; } = new();
        public InstantQuotationPart Part { get; }
        public InstantQuotationOrderQuote Quote { get; }
        public InstantQuotationFinalizedFile File { get; }

        public Boundary()
        {
            var geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
                1, new string('a', 64), 10, 20, 30, 1000, 700,
                Enumerable.Repeat(100.0, 64).ToArray(), Enumerable.Repeat(60.0, 64).ToArray(), 12, 1, true, false, false, 0.8);
            var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
            Part = new(Guid.NewGuid(), "frozen-file.stl", new(id.ToString("D")), geometry, new("ABS", "Black", 2, BuildPreference.Strength));
            Quote = SyntheticPhysicalPricingTestData.Quote(new([Part]));
            File = new(id, "private-fixture", "clean/fixture part.stl", "frozen-file.stl", "model/stl", 100, new string('a', 64));
            http = new(new Handler(this)) { BaseAddress = new("https://files.test/") };
        }

        public Task<InstantQuotationNotificationPreparationResult> PrepareAsync(InstantQuotationFinalizedFile? file = null, CancellationToken cancellationToken = default) =>
            new InstantQuotationNotificationPreparationClient(this, Tokens, TimeProvider.System) { OperationTimeout = Timeout }.PrepareAsync(
                new("session", new string('a', 64), new([Part]), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), Quote,
                new("Thai", "Customer", "customer@example.test", null, "Thailand", null, null, "Review part"), 42, [file ?? File], cancellationToken);

        public HttpClient CreateClient(string name) { Assert.Equal("files", name); return http; }
        public void Dispose() => http.Dispose();

        private sealed class Handler(Boundary owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.Paths.Add(request.RequestUri!.PathAndQuery);
                owner.AuthorizationSeen = request.Headers.Authorization?.Scheme == "Bearer";
                var response = new HttpResponseMessage((HttpStatusCode)owner.Status) { Content = owner.Content ?? JsonContent.Create(owner.Link) };
                if (owner.IncludeDate) response.Headers.Date = DateTimeOffset.UtcNow;
                return owner.Transport?.Invoke(response) ?? Task.FromResult(response);
            }
        }
    }

    private sealed class Tokens : IServiceAccessTokenProvider
    {
        public bool Invalidated { get; private set; }
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("synthetic-service-token");
        public void Invalidate(string token) => Invalidated = true;
    }
}
