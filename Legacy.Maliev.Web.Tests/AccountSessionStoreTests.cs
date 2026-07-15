using System.Text;
using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class AccountSessionStoreTests
{
    [Fact]
    public async Task SignIn_RejectsAccessTokenWithoutPositiveCustomerDatabaseId()
    {
        var now = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var store = new LockingStore(new AccountSession(
            "existing@example.com",
            7,
            "old-access",
            "old-refresh",
            now.AddMinutes(10),
            now.AddDays(1)));
        var authentication = new RefreshingAuthenticationClient(
            now,
            new CustomerAuthenticationResult(
                new CustomerTokenSet("access", "refresh", "Bearer", 900, now.AddDays(1)),
                true));
        var manager = new AccountSessionManager(authentication, store, new FixedTimeProvider(now));

        var status = await manager.SignInAsync(
            new DefaultHttpContext(),
            "customer@example.com",
            "correct-password",
            false,
            default);

        Assert.Equal(AccountSignInStatus.InvalidCredentials, status);
        Assert.Equal(0, store.SetCalls);
    }

    [Fact]
    public async Task StoredSession_IsDataProtectedAndDoesNotExposeTokens()
    {
        var services = new ServiceCollection()
            .AddDataProtection()
            .Services
            .AddDistributedMemoryCache()
            .BuildServiceProvider();
        var cache = services.GetRequiredService<IDistributedCache>();
        var store = new DistributedAccountSessionStore(
            cache,
            services.GetRequiredService<IDataProtectionProvider>(),
            services,
            TimeProvider.System,
            NullLogger<DistributedAccountSessionStore>.Instance);
        var session = new AccountSession(
            "customer@example.com",
            42,
            "sensitive-access-token",
            "sensitive-refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(1));

        await store.SetAsync("session-id", session, default);

        var raw = await cache.GetAsync("legacy:web:session:session-id");
        Assert.NotNull(raw);
        var rawText = Encoding.UTF8.GetString(raw);
        Assert.DoesNotContain("sensitive-access-token", rawText, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-refresh-token", rawText, StringComparison.Ordinal);
        Assert.Equal(session, await store.GetAsync("session-id", default));
    }

    [Fact]
    public async Task ConcurrentAccessTokenRequests_RotateRefreshTokenOnlyOnce()
    {
        var now = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var store = new LockingStore(new AccountSession(
            "customer@example.com",
            42,
            "old-access",
            "old-refresh",
            now.AddSeconds(30),
            now.AddDays(1)));
        var authentication = new RefreshingAuthenticationClient(now);
        var manager = new AccountSessionManager(authentication, store, new FixedTimeProvider(now));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(AccountSessionManager.SessionIdClaim, "session-id")],
                "test")),
        };

        var results = await Task.WhenAll(
            manager.GetAccessTokenAsync(context, default),
            manager.GetAccessTokenAsync(context, default));

        Assert.All(results, result => Assert.Equal("new-access", result));
        Assert.Equal(1, authentication.RefreshCalls);
        Assert.Equal("new-refresh", store.Session.RefreshToken);
        Assert.Equal(42, await manager.GetCustomerDatabaseIdAsync(context, default));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class LockingStore(AccountSession session) : IAccountSessionStore
    {
        private readonly SemaphoreSlim refreshLock = new(1, 1);
        public AccountSession Session { get; private set; } = session;
        public int SetCalls { get; private set; }

        public Task<AccountSession?> GetAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<AccountSession?>(Session);

        public Task SetAsync(string sessionId, AccountSession value, CancellationToken cancellationToken)
        {
            SetCalls++;
            Session = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public async ValueTask<IAsyncDisposable?> AcquireRefreshLockAsync(string sessionId, CancellationToken cancellationToken)
        {
            await refreshLock.WaitAsync(cancellationToken);
            return new Releaser(refreshLock);
        }

        private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                semaphore.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RefreshingAuthenticationClient(
        DateTimeOffset now,
        CustomerAuthenticationResult? loginResult = null) : ICustomerAuthenticationClient
    {
        public int RefreshCalls { get; private set; }

        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            RefreshCalls++;
            return Task.FromResult(new CustomerAuthenticationResult(
                new CustomerTokenSet("new-access", "new-refresh", "Bearer", 900, now.AddDays(1)),
                true));
        }

        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            loginResult is null
                ? throw new NotSupportedException()
                : Task.FromResult(loginResult);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
