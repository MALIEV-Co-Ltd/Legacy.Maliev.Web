using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationProfileCompletionClientTests
{
    private const string EntityTag = "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"";
    private static string OwnerToken(string emailJson = "\"owner@example.test\"") => "header."
        + Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"email\":" + emailJson + "}")) + ".signature";

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("2,2")]
    public async Task Read_MissingOrWrongCapability_FailsClosed(string? capability)
    {
        var handler = new Handler(capability);
        var result = await Client(handler).ReadAsync("customer:7", default);
        Assert.Null(result.Graph);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, result.ProblemCategory);
        Assert.Equal("Bearer " + OwnerToken(), handler.Authorization);
    }

    [Fact]
    public async Task Read_DisabledOrOwnerMismatch_DoesNotSendHttp()
    {
        var handler = new Handler("2");
        Assert.Null((await Client(handler, enabled: false).ReadAsync("customer:7", default)).Graph);
        Assert.Null((await Client(handler).ReadAsync("customer:8", default)).Graph);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Read_ContractTwo_UsesUniqueOwnerBoundMetadata()
    {
        var handler = new Handler("2");
        var result = await Client(handler).ReadAsync("customer:7", default);
        Assert.Equal(7, result.Graph?.Customer.Id);
        Assert.Equal(EntityTag, result.Graph?.EntityTag);
        Assert.Equal("owner@example.test", result.Graph?.TrustedEmail);
        Assert.Equal("0800000000", result.Graph?.TrustedMobile);
    }

    [Theory]
    [InlineData("{\"customerId\":8,\"email\":\"other@example.test\",\"mobile\":\"0800000000\"}")]
    [InlineData("{\"customerId\":7,\"email\":\"invalid\",\"mobile\":null}")]
    [InlineData("{\"customerId\":7,\"email\":null,\"mobile\":\" 0800000000\"}")]
    [InlineData("{\"customerId\":7,\"customerId\":7,\"email\":null,\"mobile\":null}")]
    [InlineData("not-json")]
    public async Task Read_MalformedOrCrossOwnerSelfIdentityNeverUsesCookieFallback(string identity)
    {
        var result = await Client(new Handler("2", identity: identity)).ReadAsync("customer:7", default);
        Assert.Null(result.Graph);
        Assert.NotEqual(InstantQuotationProblemCategory.None, result.ProblemCategory);
    }

    [Theory]
    [InlineData("\"stale@example.test\"")]
    [InlineData("null")]
    [InlineData("[\"owner@example.test\"]")]
    [InlineData("\"owner@example.test\",\"email\":\"owner@example.test\"")]
    [InlineData("\" owner@example.test\"")]
    public async Task Read_MissingProfileEmailAndStaleOrMalformedValidatedTokenMetadataRequiresReauthentication(string emailJson)
    {
        var result = await Client(new Handler("2"), token: OwnerToken(emailJson)).ReadAsync("customer:7", default);
        Assert.Null(result.Graph);
        Assert.Equal(InstantQuotationProblemCategory.Authorization, result.ProblemCategory);
    }

    [Fact]
    public async Task Complete_SendsPascalCaseTypedBranch_AndStableKeyWithoutPostedIdentity()
    {
        var handler = new Handler("2");
        var key = Guid.NewGuid();
        var body = new InstantQuotationProfileCompletionBody("Owner", "Name", null, "0800000000", null,
            "0115562011815", null, null, true, "branch", "00003");
        var result = await Client(handler).CompleteAsync("customer:7", new(7, key, EntityTag, body), default);
        Assert.True(result.Succeeded);
        Assert.Equal(key.ToString("D"), handler.Key);
        Assert.Equal(EntityTag, handler.IfMatch);
        Assert.Contains("\"TaxBranch\":\"branch\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"TaxBranchCode\":\"00003\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerId", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    public async Task Read_BlankStoredEmailAndAbsentCurrentIdentityEmailRequiresReauthentication(string emailJson)
    {
        var identity = $"{{\"customerId\":7,\"email\":{emailJson},\"mobile\":\"0800000000\"}}";
        var result = await Client(new Handler("2", identity: identity)).ReadAsync("customer:7", default);
        Assert.Null(result.Graph);
        Assert.Equal(InstantQuotationProblemCategory.Authorization, result.ProblemCategory);
    }

    [Fact]
    public async Task Read_PopulatedStoredEmailRemainsAuthoritativeWithAbsentCurrentIdentityEmail()
    {
        var result = await Client(new Handler("2", identity: "{\"customerId\":7,\"email\":null,\"mobile\":\"0800000000\"}",
            storedEmail: "saved@example.test")).ReadAsync("customer:7", default);
        Assert.Equal("saved@example.test", result.Graph?.Customer.Email);
        Assert.Equal("0800000000", result.Graph?.TrustedMobile);
    }

    private static InstantQuotationProfileCompletionClient Client(Handler handler, bool enabled = true, string? token = null)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "customer:7"), new Claim("identity_kind", "customer"),
            new Claim("legacy_database_id", "7"), new Claim(ClaimTypes.Email, "stale-login@example.test"),
        ], "test"));
        var factory = new Factory(handler);
        return new(factory, new Session(token ?? OwnerToken()), new HttpContextAccessor { HttpContext = context },
            Options.Create(new InstantQuotationProfileCompletionOptions { Enabled = enabled }),
            new CustomerAuthenticationClient(factory, new RejectServiceTokens(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CustomerAuthenticationClient>.Instance));
    }

    [Theory]
    [InlineData("{\"CustomerId\":7,\"CompletionId\":\"00000000-0000-0000-0000-000000000000\",\"Changed\":true}")]
    [InlineData("{\"CustomerId\":8,\"CompletionId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"Changed\":true}")]
    [InlineData("{}")]
    [InlineData("not-json")]
    public async Task Complete_InvalidReceiptNeverClaimsCompletion(string receipt)
    {
        var operation = new InstantQuotationProfileCompletionOperation(7, Guid.NewGuid(), EntityTag,
            new(null, null, null, null, null, null, null, null, true));
        Assert.False((await Client(new Handler("2", receipt)).CompleteAsync("customer:7", operation, default)).Succeeded);
    }

    private sealed class Handler(string? capability, string? receipt = null, string? identity = null, string storedEmail = "") : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Authorization { get; private set; }
        public string? Key { get; private set; }
        public string? IfMatch { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            Key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            IfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var json = request.RequestUri!.AbsolutePath.EndsWith("/identity", StringComparison.Ordinal)
                ? identity ?? "{\"customerId\":7,\"email\":\"owner@example.test\",\"mobile\":\"0800000000\"}"
                : request.Method == HttpMethod.Get ? $"{{\"Id\":7,\"FirstName\":\"Owner\",\"LastName\":\"Name\",\"Email\":\"{storedEmail}\"}}"
                : receipt ?? $"{{\"CustomerId\":7,\"CompletionId\":\"{Guid.NewGuid()}\",\"Changed\":true}}";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            response.Headers.ETag = EntityTagHeaderValue.Parse(EntityTag);
            if (capability is not null) response.Headers.Add("X-Quotation-Profile-Completion-Contract", capability);
            return response;
        }
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("https://customer.example/") };
    }
    private sealed class RejectServiceTokens : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Owner identity must never use a service token.");
        public void Invalidate(string token) => throw new NotSupportedException();
    }
    private sealed class Session(string token) : IAccountSessionManager
    {
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult<int?>(7);
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult<string?>(token);
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
