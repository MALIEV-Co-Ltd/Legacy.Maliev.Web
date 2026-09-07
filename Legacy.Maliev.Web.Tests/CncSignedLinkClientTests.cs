using System.Net;
using System.Reflection;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

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

    private static ICncSignedLinkClient CreateClient(HttpMessageHandler handler, IServiceAccessTokenProvider tokens)
    {
        var constructor = typeof(CncSignedLinkClient).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        return (ICncSignedLinkClient)constructor.Invoke([new Factory(handler), tokens]);
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
