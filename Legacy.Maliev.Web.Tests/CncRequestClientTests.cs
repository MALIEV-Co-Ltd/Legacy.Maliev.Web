using System.Net;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncRequestClientTests
{
    private static readonly Guid Journey = Guid.Parse("134b7508-1585-4180-9021-dfe008e972bf");
    private static readonly CncRequestSubmission Submission = new(new("Test", "Customer", "customer@example.test", null,
        "Thailand", null, null, "Engineering review only"), Journey);

    [Fact]
    public async Task Create_PreservesReviewOnlyPascalCaseContract()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/quotationrequests/", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.False(request.Headers.Contains("Idempotency-Key"));
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = payload.RootElement;
            Assert.Equal(Journey, root.GetProperty("JourneyId").GetGuid());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("Done").ValueKind);
            Assert.Equal("Engineering review only", root.GetProperty("Message").GetString());
            Assert.Equal("customer@example.test", root.GetProperty("Email").GetString());
            Assert.False(root.TryGetProperty("journeyId", out _));
            Assert.False(root.TryGetProperty("OrderId", out _));
            return Reply(201, $"{{\"Id\":42,\"JourneyId\":\"{Journey}\",\"Done\":null}}");
        });
        var result = await new CncRequestClient(new Factory(handler), new Tokens(), TimeProvider.System).CreateAsync(Submission, default);
        Assert.Equal(new CncRequestResult(CncRequestOutcome.Created, 42), result);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(400, "{}")]
    [InlineData(401, "{}")]
    [InlineData(409, "{}")]
    [InlineData(503, "{}")]
    [InlineData(201, "not-json")]
    [InlineData(201, "[]")]
    [InlineData(201, "{\"Id\":42,\"JourneyId\":null,\"Done\":null}")]
    [InlineData(201, "{\"Id\":0,\"JourneyId\":\"134b7508-1585-4180-9021-dfe008e972bf\",\"Done\":null}")]
    [InlineData(201, "{\"Id\":42,\"JourneyId\":\"134b7508-1585-4180-9021-dfe008e972bf\",\"Done\":false}")]
    public async Task Create_UnconfirmedResponseNeverAuthorizesRetry(int status, string body)
    {
        var handler = new Handler(_ => Task.FromResult(Reply(status, body)));
        var tokens = new Tokens();
        var result = await new CncRequestClient(new Factory(handler), tokens, TimeProvider.System).CreateAsync(Submission, default);
        Assert.Equal(CncRequestOutcome.Unknown, result.Outcome);
        Assert.Null(result.RequestId);
        Assert.Equal(1, handler.Count);
        Assert.Equal(status == 401, tokens.Invalidated);
    }

    [Fact]
    public async Task Create_PreCancelledOrMissingIdentityNeverSends()
    {
        var handler = new Handler(_ => throw new InvalidOperationException());
        var tokens = new Tokens { Token = null };
        var client = new CncRequestClient(new Factory(handler), tokens, TimeProvider.System);
        Assert.Equal(CncRequestOutcome.NotSent, (await client.CreateAsync(Submission, default)).Outcome);
        tokens.Token = "test-only-token";
        Assert.Equal(CncRequestOutcome.NotSent, (await client.CreateAsync(Submission, new CancellationToken(true))).Outcome);
        Assert.Equal(CncRequestOutcome.NotSent, (await client.CreateAsync(Submission with { JourneyId = Guid.Empty }, default)).Outcome);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Create_AcceptsOmittedNullDoneAndSourceSizeReviewRecord()
    {
        var body = JsonSerializer.Serialize(new { Id = 42, JourneyId = Journey, Message = new string('<', 262144) });
        var handler = new Handler(_ => Task.FromResult(Reply(201, body)));
        var result = await new CncRequestClient(new Factory(handler), new Tokens(), TimeProvider.System).CreateAsync(Submission, default);
        Assert.Equal(new CncRequestResult(CncRequestOutcome.Created, 42), result);
    }

    [Fact]
    public async Task Create_DisconnectionAfterSendRemainsUnknown()
    {
        var handler = new Handler(_ => throw new OperationCanceledException());
        var result = await new CncRequestClient(new Factory(handler), new Tokens(), TimeProvider.System).CreateAsync(Submission, default);
        Assert.Equal(CncRequestOutcome.Unknown, result.Outcome);
        Assert.Equal(1, handler.Count);
    }

    private static HttpResponseMessage Reply(int status, string body) => new((HttpStatusCode)status) { Content = new StringContent(body) };
    [Fact]
    public async Task Create_AuthenticationCircuitRejectedNeverSends()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("POST must not start"));
        var result = await new CncRequestClient(new Factory(handler), new RejectedTokens(), TimeProvider.System)
            .CreateAsync(Submission, default);
        Assert.Equal(new CncRequestResult(CncRequestOutcome.NotSent), result);
        Assert.Equal(0, handler.Count);
    }

    private sealed class RejectedTokens : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
            => throw new Polly.CircuitBreaker.BrokenCircuitException();
        public void Invalidate(string token) => throw new InvalidOperationException();
    }

    private sealed class Tokens : IServiceAccessTokenProvider
    {
        internal string? Token = "test-only-token";
        internal bool Invalidated;
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Token);
        public void Invalidate(string token) => Invalidated = true;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return send(request);
        }
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("quotations", name);
            return new HttpClient(handler, false) { BaseAddress = new Uri("https://quotations.example/") };
        }
    }
}
