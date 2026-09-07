using System.Net;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncFileFinalizationClientTests
{
    [Fact]
    public void Registration_ProvidesScopedServerOnlyClient()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLegacyServiceClients(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(ICncFileFinalizationClient));
        Assert.Equal(typeof(CncFileFinalizationClient), descriptor.ImplementationType);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealNamedClientResilience_DoesNotReplayPutOrPost(bool moveConfirmed)
    {
        var handler = new Handler(request => new(moveConfirmed && request.Method == HttpMethod.Put
            ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLegacyServiceClients(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddSingleton<IServiceAccessTokenProvider>(new Tokens());
        services.AddHttpClient("files").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient("quotations").ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICncFileFinalizationClient>().FinalizeAsync(Request, default);
        Assert.Equal(moveConfirmed ? CncFileFinalizationOutcome.MovedLinkUnconfirmed : CncFileFinalizationOutcome.MoveUnconfirmed, result.Outcome);
        Assert.Equal(moveConfirmed ? 2 : 1, handler.Methods.Count);
    }

    [Theory]
    [InlineData("drawing", ".pdf")]
    [InlineData("model", ".igs")]
    public async Task DrawingAndIges_UseReservedBasenameAndOneSubmissionDate(string role, string extension)
    {
        var request = Request with { File = Request.File with { Role = role, StoragePath = Source.Replace(".step", extension, StringComparison.Ordinal) } };
        var destination = Destination.Replace(".step", extension, StringComparison.Ordinal);
        var handler = new Handler(message => message.Method == HttpMethod.Put ? new(HttpStatusCode.NoContent)
            : Reply(201, JsonSerializer.Serialize(new { Id = 17, RequestId = 42, Bucket = "maliev-quotation-requests", ObjectName = destination }), "/quotationrequests/files/17"));
        Assert.Equal(new(CncFileFinalizationOutcome.Linked, destination, 17), await Client(handler).FinalizeAsync(request, default));
    }

    [Fact]
    public async Task Destination_UsesUtcDateForOffsetSubmissionTimestamp()
    {
        var request = Request with { SubmissionStartedAtUtc = new(2026, 9, 8, 0, 30, 0, TimeSpan.FromHours(7)) };
        var handler = new Handler(message =>
        {
            var query = QueryHelpers.ParseQuery(message.RequestUri!.Query);
            Assert.Equal(Destination, query[message.Method == HttpMethod.Put ? "destinationObjectName" : "objectName"]);
            return message.Method == HttpMethod.Put ? new(HttpStatusCode.NoContent) : Linked();
        });
        Assert.Equal(CncFileFinalizationOutcome.Linked, (await Client(handler).FinalizeAsync(request, default)).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Link_OversizedOrNonJsonResponseRemainsTerminal(bool oversized)
    {
        var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Put) return new(HttpStatusCode.NoContent);
            var response = Linked();
            response.Content = oversized
                ? new StringContent(new string(' ', 65537), System.Text.Encoding.UTF8, "application/json")
                : new StringContent("{}", System.Text.Encoding.UTF8, "text/plain");
            return response;
        });
        Assert.Equal(new(CncFileFinalizationOutcome.MovedLinkUnconfirmed, Destination), await Client(handler).FinalizeAsync(Request, default));
        Assert.Equal([HttpMethod.Put, HttpMethod.Post], handler.Methods);
    }

    private const string Session = "66666666-1111-2222-3333-444444444444";
    private const string Basename = "11111111222233334444555555555555.step";
    private const string Source = "2026-9-6/" + Session + "/" + Basename;
    private const string Destination = "instant-quotation/2026-9-7/" + Session + "/" + Basename;
    private static CncFileFinalizationRequest Request => new(42, Session, new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero), new(Session, Source, "model"));

    [Fact]
    public async Task Finalize_MovesBeforeLinkingExactCoordinatesWithBearerAndNoBody()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-only-token", request.Headers.Authorization.Parameter);
            Assert.Null(request.Content);
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            if (request.Method == HttpMethod.Put)
            {
                Assert.Equal("files.example", request.RequestUri.Host);
                Assert.Equal("/Uploads", request.RequestUri.AbsolutePath);
                Assert.Equal("maliev-instant-quotations", query["sourceBucket"]);
                Assert.Equal("maliev-quotation-requests", query["destinationBucket"]);
                Assert.Equal(Source, query["sourceObjectName"]);
                Assert.Equal(Destination, query["destinationObjectName"]);
                return new(HttpStatusCode.NoContent);
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("quotations.example", request.RequestUri.Host);
            Assert.Equal("/quotationrequests/42/files", request.RequestUri.AbsolutePath);
            Assert.Equal("maliev-quotation-requests", query["bucket"]);
            Assert.Equal(Destination, query["objectName"]);
            return Linked();
        });
        var result = await Client(handler).FinalizeAsync(Request, default);
        Assert.Equal(new(CncFileFinalizationOutcome.Linked, Destination, 17), result);
        Assert.Equal([HttpMethod.Put, HttpMethod.Post], handler.Methods);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Move_UnconfirmedResponseNeverLinksRetriesOrDeletes(int status)
    {
        var handler = new Handler(_ => new((HttpStatusCode)status));
        var tokens = new Tokens();
        var result = await Client(handler, tokens).FinalizeAsync(Request, default);
        Assert.Equal(CncFileFinalizationOutcome.MoveUnconfirmed, result.Outcome);
        Assert.Equal([HttpMethod.Put], handler.Methods);
        Assert.Equal(status == 401, tokens.Invalidated);
    }

    [Theory]
    [InlineData(200, "{}", "/quotationrequests/files/17")]
    [InlineData(401, "{}", "/quotationrequests/files/17")]
    [InlineData(503, "{}", "/quotationrequests/files/17")]
    [InlineData(201, "not-json", "/quotationrequests/files/17")]
    [InlineData(201, "[]", "/quotationrequests/files/17")]
    [InlineData(201, "{\"Id\":17,\"RequestId\":41,\"Bucket\":\"maliev-quotation-requests\"}", "/quotationrequests/files/17")]
    public async Task Link_UnconfirmedResponsePreservesTerminalMovedState(int status, string body, string location)
    {
        var handler = new Handler(request => request.Method == HttpMethod.Put ? new(HttpStatusCode.NoContent) : Reply(status, body, location));
        var tokens = new Tokens();
        var result = await Client(handler, tokens).FinalizeAsync(Request, default);
        Assert.Equal(CncFileFinalizationOutcome.MovedLinkUnconfirmed, result.Outcome);
        Assert.Equal(Destination, result.DestinationObjectName);
        Assert.Null(result.RequestFileId);
        Assert.Equal([HttpMethod.Put, HttpMethod.Post], handler.Methods);
        Assert.Equal(status == 401, tokens.Invalidated);
    }

    [Theory]
    [InlineData("https://quotations.example/quotationrequests/files/17", true)]
    [InlineData("https://other.example/quotationrequests/files/17", false)]
    [InlineData("https://quotations.example/quotationrequests/files/99", false)]
    [InlineData("https://quotations.example/quotationrequests/files/17?x=1", false)]
    [InlineData("https://quotations.example/quotationrequests/files/17#x", false)]
    [InlineData("http://quotations.example/quotationrequests/files/17", false)]
    [InlineData("//other.example/quotationrequests/files/17", false)]
    public async Task Link_LocationMustIdentifyExactResourceAtTrustedOrigin(string location, bool accepted)
    {
        var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Put) return new(HttpStatusCode.NoContent);
            var response = Linked();
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return response;
        });
        Assert.Equal(accepted ? CncFileFinalizationOutcome.Linked : CncFileFinalizationOutcome.MovedLinkUnconfirmed,
            (await Client(handler).FinalizeAsync(Request, default)).Outcome);
        Assert.Equal([HttpMethod.Put, HttpMethod.Post], handler.Methods);
    }

    [Theory]
    [InlineData("bucket")]
    [InlineData("object")]
    [InlineData("id")]
    [InlineData("location")]
    public async Task Link_MismatchedIdentityNeverReportsSuccess(string mismatch)
    {
        var body = JsonSerializer.Serialize(new
        {
            Id = mismatch == "id" ? 0 : 17,
            RequestId = 42,
            Bucket = mismatch == "bucket" ? "wrong" : "maliev-quotation-requests",
            ObjectName = mismatch == "object" ? Source : Destination
        });
        var handler = new Handler(request => request.Method == HttpMethod.Put ? new(HttpStatusCode.NoContent)
            : Reply(201, body, mismatch == "location" ? "/quotationrequests/files/999" : "/quotationrequests/files/17"));
        Assert.Equal(CncFileFinalizationOutcome.MovedLinkUnconfirmed, (await Client(handler).FinalizeAsync(Request, default)).Outcome);
        Assert.Equal(2, handler.Methods.Count);
    }

    [Theory]
    [InlineData("2026-9-6/other/11111111222233334444555555555555.step")]
    [InlineData(" 2026-9-6/66666666-1111-2222-3333-444444444444/11111111222233334444555555555555.step")]
    [InlineData("2026-9-6//66666666-1111-2222-3333-444444444444/11111111222233334444555555555555.step")]
    [InlineData("2026-9-6/66666666-1111-2222-3333-444444444444/../part.step")]
    [InlineData("2026-9-6\\66666666-1111-2222-3333-444444444444\\11111111222233334444555555555555.step")]
    [InlineData("2026-9-6/66666666-1111-2222-3333-444444444444/11111111222233334444555555555555.STEP")]
    [InlineData("2026-9-6/66666666-1111-2222-3333-444444444444/11111111222233334444555555555555.step?x=1")]
    public async Task InvalidPath_NeverSends(string path)
    {
        var handler = new Handler(_ => throw new InvalidOperationException());
        var result = await Client(handler).FinalizeAsync(Request with { File = Request.File with { StoragePath = path } }, default);
        Assert.Equal(CncFileFinalizationOutcome.NotSent, result.Outcome);
        Assert.Empty(handler.Methods);
    }

    [Fact]
    public async Task InvalidSessionRoleOrRequest_NeverSends()
    {
        var handler = new Handler(_ => throw new InvalidOperationException());
        foreach (var request in new[] { Request with { RequestId = 0 }, Request with { SessionId = Guid.NewGuid().ToString() },
            Request with { File = Request.File with { SessionId = Guid.NewGuid().ToString() } },
            Request with { File = Request.File with { Role = "drawing" } }, Request with { SubmissionStartedAtUtc = default } })
            Assert.Equal(CncFileFinalizationOutcome.NotSent, (await Client(handler).FinalizeAsync(request, default)).Outcome);
        Assert.Empty(handler.Methods);
    }

    [Fact]
    public async Task MissingTokenAuthCircuitAndPreCancellation_NeverSends()
    {
        var handler = new Handler(_ => throw new InvalidOperationException());
        Assert.Equal(CncFileFinalizationOutcome.NotSent, (await Client(handler).FinalizeAsync(Request, new(true))).Outcome);
        Assert.Equal(CncFileFinalizationOutcome.NotSent, (await Client(handler, new Tokens { Token = null }).FinalizeAsync(Request, default)).Outcome);
        Assert.Equal(CncFileFinalizationOutcome.NotSent, (await Client(handler, new Tokens { Fail = true }).FinalizeAsync(Request, default)).Outcome);
        Assert.Empty(handler.Methods);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterSend_NeverRetriesOrDeletes(bool afterMove)
    {
        var handler = new Handler(request => afterMove && request.Method == HttpMethod.Put ? new(HttpStatusCode.NoContent) : throw new OperationCanceledException());
        var result = await Client(handler).FinalizeAsync(Request, default);
        Assert.Equal(afterMove ? CncFileFinalizationOutcome.MovedLinkUnconfirmed : CncFileFinalizationOutcome.MoveUnconfirmed, result.Outcome);
        Assert.Equal(afterMove ? 2 : 1, handler.Methods.Count);
        Assert.DoesNotContain(HttpMethod.Delete, handler.Methods);
    }

    private static CncFileFinalizationClient Client(Handler handler, Tokens? tokens = null) => new(new Factory(handler), tokens ?? new Tokens());
    private static HttpResponseMessage Linked() => Reply(201, JsonSerializer.Serialize(new { Id = 17, RequestId = 42, Bucket = "maliev-quotation-requests", ObjectName = Destination }), "/quotationrequests/files/17");
    private static HttpResponseMessage Reply(int status, string body, string location)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        response.Headers.Location = new Uri(location, UriKind.Relative);
        return response;
    }
    private sealed class Tokens : IServiceAccessTokenProvider
    {
        internal string? Token = "test-only-token";
        internal bool Fail;
        internal bool Invalidated;
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Fail ? throw new BrokenCircuitException() : ValueTask.FromResult(Token);
        public void Invalidate(string token) => Invalidated = true;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal readonly List<HttpMethod> Methods = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return Task.FromResult(respond(request));
        }
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri($"https://{name}.example/") };
    }
}
