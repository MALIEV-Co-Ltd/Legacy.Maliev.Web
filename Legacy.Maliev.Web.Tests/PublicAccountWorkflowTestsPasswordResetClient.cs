using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.Timeout;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Controlled Web-client responses, not Auth producer status or reset-token qualification.</summary>
public sealed class PublicAccountWorkflowTestsPasswordResetClient
{
    internal const string Email = "reset-boundary@example.invalid";
    internal const string Token = "synthetic-reset-token-abcdefghijklmnopqrstuvwxyz123456";
    internal const string Password = "synthetic-reset-password";
    internal const string PrivateDetail = "private-provider-reset-detail";

    public static IEnumerable<object[]> StatusCases
    {
        get
        {
            foreach (var status in Enumerable.Range(200, 100))
                yield return new object[] { status, (int)PasswordResetCompletionOutcome.Completed };
            foreach (var status in new[] { 400, 404, 409, 418, 422 })
                yield return new object[] { status, (int)PasswordResetCompletionOutcome.UnclassifiedFailure };
            foreach (var status in new[] { 401, 403 })
                yield return new object[] { status, (int)PasswordResetCompletionOutcome.AuthenticationRejected };
            yield return new object[] { 429, (int)PasswordResetCompletionOutcome.RateLimited };
            foreach (var status in new[] { 500, 503, 599 })
                yield return new object[] { status, (int)PasswordResetCompletionOutcome.ServiceUnavailable };
            foreach (var status in new[] { 302, 600 })
                yield return new object[] { status, (int)PasswordResetCompletionOutcome.UnexpectedResponse };
        }
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public async Task ResetOverride_ClassifiesObservedStatusWithoutChangingLegacyBooleanOrWire(
        int status, int expected)
    {
        using var actual = new ClientFixture(status);
        var outcome = await actual.Contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, default);
        Assert.Equal((PasswordResetCompletionOutcome)expected, outcome);
        await AssertWireAndDisposal(actual);
        Assert.Equal(status is 401 or 403 ? 1 : 0, actual.Credentials.Invalidations);

        // Separate invocation/fixture: this freezes the old Boolean contract, not a producer claim.
        using var legacy = new ClientFixture(status);
        Assert.Equal(status is >= 200 and < 300,
            await legacy.Contract.CompletePasswordResetAsync(Email, Token, Password, default));
        await AssertWireAndDisposal(legacy);
        Assert.Equal(status is 401 or 403 ? 1 : 0, legacy.Credentials.Invalidations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task ResetOverride_MissingServiceCredentialsNeverCreatesAnHttpRequest(string? credential)
    {
        using var fixture = new ClientFixture(200);
        fixture.Credentials.Token = credential;
        Assert.Equal(PasswordResetCompletionOutcome.CredentialsUnavailable,
            await fixture.Contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, default));
        Assert.Equal(1, fixture.Credentials.Acquisitions);
        Assert.Equal(0, fixture.Factory.Calls);
        Assert.Equal(0, fixture.Handler.Calls);
        Assert.Equal(0, fixture.Credentials.Invalidations);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("http-timeout")]
    [InlineData("polly-timeout")]
    public async Task ResetOverride_KnownSendFailuresDoNotInventAnObservedHttpStatus(string mode)
    {
        using var fixture = new ClientFixture(200);
        Exception failure = mode switch
        {
            "http" => new HttpRequestException(PrivateDetail),
            "http-timeout" => new TaskCanceledException(PrivateDetail, new TimeoutException(PrivateDetail)),
            _ => new TimeoutRejectedException(PrivateDetail),
        };
        fixture.Handler.SendResponse = _ => Task.FromException<HttpResponseMessage>(failure);
        Assert.Equal(PasswordResetCompletionOutcome.TransportUnavailable,
            await fixture.Contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, default));
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Equal(0, fixture.Credentials.Invalidations);
        await AssertRequestDisposed(fixture.Handler.Last!);
    }

    [Theory]
    [InlineData("unexpected")]
    [InlineData("noncaller-task-cancel")]
    [InlineData("noncaller-operation-cancel")]
    public async Task ResetOverride_UnexpectedSendFailuresRemainExceptionsRatherThanInvalidLinkClaims(string mode)
    {
        using var fixture = new ClientFixture(200);
        Exception failure = mode switch
        {
            "unexpected" => new InvalidOperationException(PrivateDetail),
            "noncaller-task-cancel" => new TaskCanceledException(PrivateDetail),
            _ => new OperationCanceledException(PrivateDetail),
        };
        fixture.Handler.SendResponse = _ => Task.FromException<HttpResponseMessage>(failure);
        var observed = await Record.ExceptionAsync(() =>
            fixture.Contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, default));
        Assert.Same(failure, observed);
        Assert.Equal(1, fixture.Handler.Calls);
        await AssertRequestDisposed(fixture.Handler.Last!);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("unexpected")]
    [InlineData("caller-cancel")]
    public async Task ResetOverride_TokenAcquisitionFailureIsNotFabricatedAsMissingCredentials(string mode)
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new ClientFixture(200);
        Exception failure = mode switch
        {
            "http" => new HttpRequestException(PrivateDetail),
            "unexpected" => new InvalidOperationException(PrivateDetail),
            _ => new OperationCanceledException(cancellation.Token),
        };
        if (mode == "caller-cancel") cancellation.Cancel();
        fixture.Credentials.Acquire = _ => ValueTask.FromException<string?>(failure);
        Assert.Same(failure, await Record.ExceptionAsync(() =>
            fixture.Contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, cancellation.Token)));
        Assert.Equal(cancellation.Token, fixture.Credentials.LastCancellation);
        Assert.Equal(0, fixture.Factory.Calls);
        Assert.Equal(0, fixture.Handler.Calls);
    }

    [Fact]
    public async Task ResetOverride_CallerCancellationDuringSendSettlesOriginalTaskAndDisposesRequest()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new ClientFixture(200);
        fixture.Handler.SendResponse = token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Equal(cancellation.Token, fixture.Credentials.LastCancellation);
        await AssertRequestDisposed(fixture.Handler.Last!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultBridge_CallsExistingBooleanMethodExactlyOnce(bool completed)
    {
        using var cancellation = new CancellationTokenSource();
        var legacy = new ResetPasswordLegacyBoundary { Result = completed };
        ICustomerAuthenticationClient contract = legacy;
        Assert.Equal(completed ? PasswordResetCompletionOutcome.Completed : PasswordResetCompletionOutcome.UnclassifiedFailure,
            await contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, cancellation.Token));
        Assert.Equal(1, legacy.LegacyCalls);
        Assert.Equal((Email, Token, Password), legacy.LastArguments);
        Assert.Equal(cancellation.Token, legacy.LastCancellation);
    }

    [Fact]
    public async Task DefaultBridge_PreservesUnexpectedExceptionIdentity()
    {
        var failure = new InvalidOperationException(PrivateDetail);
        var legacy = new ResetPasswordLegacyBoundary { Failure = failure };
        Assert.Same(failure, await Record.ExceptionAsync(() =>
            ((ICustomerAuthenticationClient)legacy).CompletePasswordResetOutcomeAsync(Email, Token, Password, default)));
        Assert.Equal(1, legacy.LegacyCalls);
    }

    [Fact]
    public async Task DefaultBridge_PassesCallerCancellationToTheOriginalOperation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var legacy = new ResetPasswordLegacyBoundary
        {
            Failure = new OperationCanceledException(cancellation.Token),
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ((ICustomerAuthenticationClient)legacy).CompletePasswordResetOutcomeAsync(Email, Token, Password, cancellation.Token));
        Assert.Equal(1, legacy.LegacyCalls);
        Assert.Equal(cancellation.Token, legacy.LastCancellation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetOverride_ResponseDisposalCannotReplaceInvalidationOrCallerCancellationFailure(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new ClientFixture(401);
        Exception primary = cancel
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException(PrivateDetail);
        fixture.Credentials.OnInvalidate = () =>
        {
            if (cancel) cancellation.Cancel();
            throw primary;
        };
        fixture.Body.ThrowOnDispose = true;
        try
        {
            Assert.Same(primary, await Record.ExceptionAsync(() =>
                fixture.Contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, cancellation.Token)));
            Assert.True(fixture.Body.Disposed);
            await AssertRequestDisposed(fixture.Handler.Last!);
        }
        finally
        {
            fixture.Body.ThrowOnDispose = false;
        }
    }

    [Fact]
    public async Task ResetOverride_DisposalFailureWithoutEarlierFailureIsNotReportedAsCompleted()
    {
        using var fixture = new ClientFixture(200);
        fixture.Body.ThrowOnDispose = true;
        try
        {
            Assert.Same(fixture.Body.DisposalFailure, await Record.ExceptionAsync(() =>
                fixture.Contract.CompletePasswordResetOutcomeAsync(Email, Token, Password, default)));
            Assert.True(fixture.Body.Disposed);
            await AssertRequestDisposed(fixture.Handler.Last!);
        }
        finally
        {
            fixture.Body.ThrowOnDispose = false;
        }
    }

    private static async Task AssertWireAndDisposal(ClientFixture fixture)
    {
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Equal("auth", fixture.Factory.Name);
        Assert.Equal(1, fixture.Credentials.Acquisitions);
        var request = Assert.IsType<HttpRequestMessage>(fixture.Handler.Last);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/auth/v1/customer-self-service/password-reset/complete", request.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer synthetic-service-token", request.Headers.Authorization?.ToString());
        using var body = JsonDocument.Parse(fixture.Handler.Body);
        Assert.Equal(new[] { "email", "password", "token" },
            body.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(Email, body.RootElement.GetProperty("email").GetString());
        Assert.Equal(Token, body.RootElement.GetProperty("token").GetString());
        Assert.Equal(Password, body.RootElement.GetProperty("password").GetString());
        Assert.True(fixture.Body.Disposed);
        await AssertRequestDisposed(request);
    }

    private static async Task AssertRequestDisposed(HttpRequestMessage request) =>
        await Assert.ThrowsAsync<ObjectDisposedException>(() => request.Content!.ReadAsStringAsync());

    private sealed class ClientFixture : IDisposable
    {
        public ClientFixture(int status)
        {
            Body = new DisposalProbe();
            Response = new HttpResponseMessage((HttpStatusCode)status) { Content = Body };
            Handler = new ObservingHandler(_ => Task.FromResult(Response));
            Factory = new NamedFactory(Handler);
            Contract = new CustomerAuthenticationClient(Factory, Credentials, NullLogger<CustomerAuthenticationClient>.Instance);
        }
        public DisposalProbe Body { get; }
        public HttpResponseMessage Response { get; }
        public ObservingHandler Handler { get; }
        public NamedFactory Factory { get; }
        public Credentials Credentials { get; } = new();
        public ICustomerAuthenticationClient Contract { get; }
        public void Dispose()
        {
            Body.ThrowOnDispose = false;
            Response.Dispose();
            Factory.Client?.Dispose();
            Handler.Dispose();
        }
    }

    private sealed class DisposalProbe : HttpContent
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(PrivateDetail);
        public bool Disposed { get; private set; }
        public bool ThrowOnDispose { get; set; }
        public Exception DisposalFailure { get; } = new InvalidOperationException("controlled-response-disposal-failure");
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = bytes.Length; return true; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
            if (ThrowOnDispose) throw DisposalFailure;
        }
    }

    private sealed class ObservingHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public Func<CancellationToken, Task<HttpResponseMessage>> SendResponse { get; set; } = send;
        public int Calls { get; private set; }
        public HttpRequestMessage? Last { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Last = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await SendResponse(cancellationToken);
        }
    }

    private sealed class NamedFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public string? Name { get; private set; }
        public int Calls { get; private set; }
        public HttpClient? Client { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Calls++;
            Name = name;
            return Client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://auth.test/") };
        }
    }

    private sealed class Credentials : IServiceAccessTokenProvider
    {
        public string? Token { get; set; } = "synthetic-service-token";
        public Func<CancellationToken, ValueTask<string?>>? Acquire { get; set; }
        public Action? OnInvalidate { get; set; }
        public int Acquisitions { get; private set; }
        public int Invalidations { get; private set; }
        public CancellationToken LastCancellation { get; private set; }
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            Acquisitions++;
            LastCancellation = cancellationToken;
            return Acquire?.Invoke(cancellationToken) ?? ValueTask.FromResult(Token);
        }
        public void Invalidate(string token)
        {
            Assert.Equal(Token, token);
            Invalidations++;
            OnInvalidate?.Invoke();
        }
    }
}

internal class ResetPasswordLegacyBoundary : ICustomerAuthenticationClient
{
    internal bool Result { get; set; }
    internal Exception? Failure { get; set; }
    internal int LegacyCalls { get; private set; }
    internal (string Email, string Token, string Password) LastArguments { get; private set; }
    internal CancellationToken LastCancellation { get; private set; }
    public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken)
    {
        LegacyCalls++;
        LastArguments = (email, token, password);
        LastCancellation = cancellationToken;
        return Failure is null ? Task.FromResult(Result) : Task.FromException<bool>(Failure);
    }
    public Task<CustomerSelfIdentityResult> GetSelfIdentityAsync(string accessToken, int expectedCustomerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerEmailChangeValidationResult> ValidateEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerEmailChangeCompletionResult> CompleteEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
}
