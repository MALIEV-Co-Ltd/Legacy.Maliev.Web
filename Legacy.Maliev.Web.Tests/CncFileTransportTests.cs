using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncFileTransportTests
{
    [Fact]
    public async Task Upload_CancelledBeforeSend_IsDefinitivelyNotSent()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("No HTTP request is allowed."));
        var client = new CncFileTransport(new Factory(handler), new Tokens());
        var result = await client.UploadAsync("date/session/part.pdf", [1], "application/pdf", new CancellationToken(true));
        Assert.Equal(CncUploadTransportOutcome.NotSent, result.Outcome);
        Assert.Null(result.StatusCode);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public void ServiceRegistration_ProvidesScopedCncTransport()
    {
        var services = new ServiceCollection();
        services.AddLegacyServiceClients(new ConfigurationBuilder().Build());
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(ICncFileTransport));
        Assert.Equal(typeof(CncFileTransport), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Theory]
    [InlineData(201, "{\"Object\":[{\"Bucket\":\"maliev-instant-quotations\",\"ObjectName\":\"date/session/part.pdf\",\"Uri\":\"https://storage.example/file\"}]}", CncUploadTransportOutcome.Uploaded)]
    [InlineData(201, "{\"Object\":[{\"Bucket\":\"wrong\",\"ObjectName\":\"date/session/part.pdf\",\"Uri\":\"https://storage.example/file\"}]}", CncUploadTransportOutcome.Unknown)]
    [InlineData(201, "{\"object\":[]}", CncUploadTransportOutcome.Unknown)]
    [InlineData(201, "not-json", CncUploadTransportOutcome.Unknown)]
    [InlineData(401, "", CncUploadTransportOutcome.Rejected)]
    [InlineData(400, "", CncUploadTransportOutcome.Rejected)]
    [InlineData(403, "", CncUploadTransportOutcome.Rejected)]
    [InlineData(404, "", CncUploadTransportOutcome.Rejected)]
    [InlineData(413, "", CncUploadTransportOutcome.Rejected)]
    [InlineData(415, "", CncUploadTransportOutcome.Rejected)]
    [InlineData(422, "", CncUploadTransportOutcome.Rejected)]
    [InlineData(409, "", CncUploadTransportOutcome.Unknown)]
    [InlineData(429, "", CncUploadTransportOutcome.Unknown)]
    [InlineData(503, "", CncUploadTransportOutcome.Unknown)]
    public async Task Upload_PreservesPascalCaseGenericContract_AndDoesNotReplay(int status, string body, CncUploadTransportOutcome expected)
    {
        var tokens = new Tokens();
        var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            Assert.Equal("/Uploads?bucket=maliev-instant-quotations&path=date%2Fsession", request.RequestUri!.PathAndQuery);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var file = Assert.Single(form);
            Assert.Equal("files", file.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("part.pdf", file.Headers.ContentDisposition.FileName!.Trim('"'));
            Assert.Equal("application/pdf", file.Headers.ContentType!.MediaType);
            Assert.Equal(new byte[] { 1, 2 }, await file.ReadAsByteArrayAsync());
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        });
        var client = new CncFileTransport(new Factory(handler), tokens);
        var result = await client.UploadAsync("date/session/part.pdf", [1, 2], "application/pdf", default);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal((HttpStatusCode)status, result.StatusCode);
        Assert.Equal(1, handler.Count);
        Assert.Equal(status == 401, tokens.Invalidated);
    }

    [Fact]
    public async Task Upload_SendFailureIsUnknown_MissingTokenIsNotSent()
    {
        var handler = new Handler(_ => throw new HttpRequestException());
        var tokens = new Tokens();
        var client = new CncFileTransport(new Factory(handler), tokens);
        Assert.Equal(CncUploadTransportOutcome.Unknown, (await client.UploadAsync("d/s/f.step", [1], "application/octet-stream", default)).Outcome);
        tokens.Token = null;
        Assert.Equal(CncUploadTransportOutcome.NotSent, (await client.UploadAsync("d/s/f.step", [1], "application/octet-stream", default)).Outcome);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(204, true)]
    [InlineData(404, false)]
    [InlineData(401, false)]
    [InlineData(503, false)]
    public async Task Cleanup_OnlyExactPath204ConfirmsDeletion(int status, bool confirmed)
    {
        var tokens = new Tokens();
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            Assert.Equal("/Uploads?bucket=maliev-instant-quotations&objectName=date%2Fsession%2Fpart.pdf", request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        });
        Assert.Equal(confirmed, await new CncFileTransport(new Factory(handler), tokens).DeleteReservedObjectAsync("date/session/part.pdf", default));
        Assert.Equal(status == 401, tokens.Invalidated);
    }

    [Theory]
    [InlineData("{\"Bucket\":\"maliev-instant-quotations\",\"Uri\":\"https://storage.example/file\"}")]
    [InlineData("{\"Bucket\":\"maliev-instant-quotations\",\"ObjectName\":\"other/path\",\"Uri\":\"https://storage.example/file\"}")]
    [InlineData("{\"Bucket\":null,\"ObjectName\":\"date/session/part.pdf\",\"Uri\":\"https://storage.example/file\"}")]
    [InlineData("{\"Bucket\":42,\"ObjectName\":\"date/session/part.pdf\",\"Uri\":\"https://storage.example/file\"}")]
    [InlineData("{\"Bucket\":\"maliev-instant-quotations\",\"ObjectName\":null,\"Uri\":\"https://storage.example/file\"}")]
    [InlineData("{\"Bucket\":\"maliev-instant-quotations\",\"ObjectName\":false,\"Uri\":\"https://storage.example/file\"}")]
    [InlineData("null")]
    public async Task Upload_InvalidObjectShapeRequiresReconciliation(string item)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        { Content = new StringContent("{\"Object\":[" + item + "]}") }));
        var result = await new CncFileTransport(new Factory(handler), new Tokens()).UploadAsync("date/session/part.pdf", [1], "application/pdf", default);
        Assert.Equal(CncUploadTransportOutcome.Unknown, result.Outcome);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_MultipleObjectsOrOversizedReplyRequiresReconciliation(bool oversized)
    {
        const string item = "{\"Bucket\":\"maliev-instant-quotations\",\"ObjectName\":\"date/session/part.pdf\",\"Uri\":\"https://storage.example/file\"}";
        var body = oversized ? new string(' ', 65537) : "{\"Object\":[" + item + "," + item + "]}";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(body) }));
        Assert.Equal(CncUploadTransportOutcome.Unknown,
            (await new CncFileTransport(new Factory(handler), new Tokens()).UploadAsync("date/session/part.pdf", [1], "application/pdf", default)).Outcome);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("../part.pdf", false)]
    [InlineData("date//part.pdf", false)]
    [InlineData("part.pdf", false)]
    [InlineData("date/session/PART.pdf", false)]
    [InlineData(" date/session/part.pdf", false)]
    [InlineData("date/session/part.pdf ", false)]
    [InlineData("date/session/part.pdf", true)]
    public async Task Upload_InvalidPathOrEmptyDataDoesNotSend(string path, bool empty)
    {
        var handler = new Handler(_ => throw new InvalidOperationException("Must not send"));
        var result = await new CncFileTransport(new Factory(handler), new Tokens()).UploadAsync(path, empty ? [] : [1], "application/pdf", default);
        Assert.Equal(CncUploadTransportOutcome.NotSent, result.Outcome);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Upload_CancelledAfterSendRetainsUnknownOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        Assert.Equal(CncUploadTransportOutcome.Unknown,
            (await new CncFileTransport(new Factory(handler), new Tokens()).UploadAsync("date/session/part.pdf", [1], "application/pdf", cancellation.Token)).Outcome);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Cleanup_TimeoutDoesNotConfirmDeletion()
    {
        var handler = new Handler(_ => throw new TaskCanceledException("timeout"));
        Assert.False(await new CncFileTransport(new Factory(handler), new Tokens()).DeleteReservedObjectAsync("date/session/part.pdf", default));
        Assert.Equal(1, handler.Count);
    }

    private sealed class Tokens : IServiceAccessTokenProvider
    {
        public string? Token { get; set; } = "test-token";
        public bool Invalidated { get; private set; }
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Token);
        public void Invalidate(string token) => Invalidated = true;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Count++; return send(request); }
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        { Assert.Equal("files", name); return new HttpClient(handler, false) { BaseAddress = new Uri("https://files.example/") }; }
    }
}
