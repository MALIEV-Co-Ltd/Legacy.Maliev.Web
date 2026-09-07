using System.Net;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerSelfIdentityClientTests
{
    [Theory]
    [InlineData("{\"customerId\":42,\"email\":\"current@example.com\",\"mobile\":\"+66812345678\"}", "current@example.com", "+66812345678")]
    [InlineData("{\"customerId\":42,\"email\":null,\"mobile\":null}", null, null)]
    [InlineData("{\"customerId\":42,\"email\":null,\"mobile\":null,\"futureField\":true}", null, null)]
    public async Task GetSelfIdentity_ValidJson_UsesOnlyCustomerBearerAndExpectedWire(string body, string? email, string? mobile)
    {
        using var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var logger = new RecordingLogger();
        var client = CreateClient(handler, logger);
        var result = await client.GetSelfIdentityAsync("customer-access-token", 42, default);

        Assert.Equal(CustomerSelfIdentityStatus.Succeeded, result.Status);
        Assert.Equal(new CustomerSelfIdentity(42, email, mobile), result.Identity);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/auth/v1/customer-self-service/identity", handler.Path);
        Assert.Equal("Bearer customer-access-token", handler.Authorization);
        Assert.False(handler.HasContent);
        Assert.Empty(logger.Messages);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"customerId\":42}")]
    [InlineData("{\"customerId\":42,\"email\":null}")]
    [InlineData("{\"CustomerId\":42,\"email\":null,\"mobile\":null}")]
    [InlineData("{\"customerId\":\"42\",\"email\":null,\"mobile\":null}")]
    [InlineData("{\"customerId\":0,\"email\":null,\"mobile\":null}")]
    [InlineData("{\"customerId\":42.5,\"email\":null,\"mobile\":null}")]
    [InlineData("{\"customerId\":42,\"email\":true,\"mobile\":null}")]
    [InlineData("{\"customerId\":42,\"email\":null,\"mobile\":123}")]
    [InlineData("{\"customerId\":42,\"customerId\":42,\"email\":null,\"mobile\":null}")]
    public async Task GetSelfIdentity_InvalidJson_FailsClosedWithoutIdentity(string body)
    {
        using var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, body));
        var result = await CreateClient(handler).GetSelfIdentityAsync("customer-token", 42, default);
        Assert.Equal(CustomerSelfIdentityStatus.InvalidResponse, result.Status);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task GetSelfIdentity_MismatchedCustomer_FailsClosedWithoutContactFields()
    {
        using var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"customerId\":99,\"email\":\"other@example.com\",\"mobile\":\"private\"}"));
        var result = await CreateClient(handler).GetSelfIdentityAsync("customer-token", 42, default);
        Assert.Equal(CustomerSelfIdentityStatus.IdentityMismatch, result.Status);
        Assert.Null(result.Identity);
    }

    [Theory]
    [InlineData(401, CustomerSelfIdentityStatus.NotAuthorized)]
    [InlineData(403, CustomerSelfIdentityStatus.NotAuthorized)]
    [InlineData(404, CustomerSelfIdentityStatus.NotFound)]
    [InlineData(429, CustomerSelfIdentityStatus.Unavailable)]
    [InlineData(500, CustomerSelfIdentityStatus.Unavailable)]
    [InlineData(503, CustomerSelfIdentityStatus.Unavailable)]
    [InlineData(400, CustomerSelfIdentityStatus.InvalidResponse)]
    [InlineData(204, CustomerSelfIdentityStatus.InvalidResponse)]
    public async Task GetSelfIdentity_FailureStatus_PreservesCategoryWithoutReadingBody(int status, CustomerSelfIdentityStatus expected)
    {
        using var handler = new RecordingHandler(_ => Json((HttpStatusCode)status, "private-provider-failure"));
        var logger = new RecordingLogger();
        var result = await CreateClient(handler, logger).GetSelfIdentityAsync("customer-token", 42, default);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Identity);
        Assert.Empty(logger.Messages);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("canceled-timeout")]
    [InlineData("pipeline-timeout")]
    [InlineData("open-circuit")]
    public async Task GetSelfIdentity_TransportFailure_ReturnsUnavailableWithoutLoggingExceptionData(string failure)
    {
        using var handler = new RecordingHandler(_ => throw (failure switch
        {
            "network" => new HttpRequestException("private-contact-details"),
            "timeout" => new TimeoutException("private-contact-details"),
            "pipeline-timeout" => new Polly.Timeout.TimeoutRejectedException("private-contact-details"),
            "open-circuit" => new Polly.CircuitBreaker.BrokenCircuitException("private-contact-details"),
            _ => new TaskCanceledException("private-contact-details"),
        }));
        var logger = new RecordingLogger();
        var result = await CreateClient(handler, logger).GetSelfIdentityAsync("customer-token", 42, default);
        Assert.Equal(CustomerSelfIdentityStatus.Unavailable, result.Status);
        Assert.Null(result.Identity);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task GetSelfIdentity_CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler).GetSelfIdentityAsync("customer-token", 42, cancellation.Token));
    }

    [Fact]
    public async Task GetSelfIdentity_MissingToken_DoesNotSendOrRequestServiceToken()
    {
        using var handler = new RecordingHandler(_ => throw new InvalidOperationException("No HTTP request expected."));
        var result = await CreateClient(handler).GetSelfIdentityAsync(" ", 42, default);
        Assert.Equal(CustomerSelfIdentityStatus.NotAuthorized, result.Status);
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task GetSelfIdentity_InvalidExpectedCustomer_DoesNotSend()
    {
        using var handler = new RecordingHandler(_ => throw new InvalidOperationException("No HTTP request expected."));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateClient(handler).GetSelfIdentityAsync("customer-token", 0, default));
        Assert.Null(handler.Method);
    }

    private static ICustomerAuthenticationClient CreateClient(RecordingHandler handler, RecordingLogger? logger = null) => new CustomerAuthenticationClient(
        new ClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://auth.test/") }),
        new RejectServiceTokenProvider(), logger ?? new RecordingLogger());

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Assert.Equal("auth", name); return client; }
    }

    private sealed class RejectServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Customer read must never request a service token.");
        public void Invalidate(string token) => throw new InvalidOperationException("Customer read must never invalidate service tokens.");
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public bool HasContent { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            HasContent = request.Content is not null;
            return Task.FromResult(response(request));
        }
    }

    private sealed class RecordingLogger : ILogger<CustomerAuthenticationClient>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
