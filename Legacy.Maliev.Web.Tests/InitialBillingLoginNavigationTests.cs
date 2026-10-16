using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Controlled initial-GET transport faults; no browser or database is started.</summary>
public sealed class InitialBillingLoginNavigationTests
{
    private static readonly Uri Login = new("https://127.0.0.1:39983/Account/Login?culture=en&returnUrl=%2FMember%2FAccount%2FManage%2FAddress%3Fculture%3Dth");

    [Fact]
    public async Task Success_NavigatesOnce()
    {
        var calls = 0;
        await InitialBillingLoginNavigation.NavigateAsync(Login, (uri, timeout) =>
        {
            Assert.Equal(Login, uri);
            Assert.InRange(timeout, 1, 30000);
            calls++;
            return Task.CompletedTask;
        });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SettledNetworkChange_RetriesIdenticalGetWithOnlyRemainingBudget()
    {
        var clock = new ControlledClock();
        var calls = 0;
        await InitialBillingLoginNavigation.NavigateAsync(Login, (uri, timeout) =>
        {
            Assert.Equal(Login, uri);
            calls++;
            Assert.Equal(calls == 1 ? 30000f : 23000f, timeout);
            clock.Advance(TimeSpan.FromSeconds(7));
            return calls == 1 ? Task.FromException(NetworkChange()) : Task.CompletedTask;
        }, clock);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RepeatedNetworkChange_StopsAfterSecondAttemptAndPreservesFailure()
    {
        var failure = NetworkChange();
        var calls = 0;
        var actual = await Assert.ThrowsAsync<PlaywrightException>(() => InitialBillingLoginNavigation.NavigateAsync(Login, (_, _) =>
        {
            calls++;
            return Task.FromException(failure);
        }));
        Assert.Same(failure, actual);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("net::ERR_CONNECTION_REFUSED")]
    [InlineData("net::ERR_NETWORK_CHANGED_SUFFIX")]
    [InlineData("Timeout 30000ms exceeded")]
    public async Task OtherTransportFailures_AreNotRetried(string message)
    {
        var failure = new PlaywrightException(message + " at " + Login.AbsoluteUri);
        var calls = 0;
        Assert.Same(failure, await Assert.ThrowsAsync<PlaywrightException>(() => InitialBillingLoginNavigation.NavigateAsync(Login, (_, _) =>
        {
            calls++;
            return Task.FromException(failure);
        })));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancellation_IsNotRetried()
    {
        var calls = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => InitialBillingLoginNavigation.NavigateAsync(Login, (_, _) =>
        {
            calls++;
            return Task.FromException(new OperationCanceledException());
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExhaustedBudget_PreservesFirstFailureWithoutAnotherAttempt()
    {
        var clock = new ControlledClock();
        var failure = NetworkChange();
        var calls = 0;
        Assert.Same(failure, await Assert.ThrowsAsync<PlaywrightException>(() => InitialBillingLoginNavigation.NavigateAsync(Login, (_, _) =>
        {
            calls++;
            clock.Advance(TimeSpan.FromSeconds(30));
            return Task.FromException(failure);
        }, clock)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PendingFirstNavigation_CannotStartAnotherAttempt()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var navigation = InitialBillingLoginNavigation.NavigateAsync(Login, (_, _) =>
        {
            calls++;
            return calls == 1 ? first.Task : Task.CompletedTask;
        });
        try { Assert.False(navigation.IsCompleted); Assert.Equal(1, calls); }
        finally { first.TrySetException(NetworkChange()); await navigation; }
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("https://example.test/Account/Login")]
    [InlineData("https://127.0.0.1:39983/Member/Account/Manage/Address")]
    [InlineData("http://127.0.0.1:39983/Account/Login")]
    public async Task NonInitialLoginTargets_AreRejectedBeforeNavigation(string target)
    {
        var invoked = false;
        await Assert.ThrowsAsync<ArgumentException>(() => InitialBillingLoginNavigation.NavigateAsync(new Uri(target), (_, _) =>
        {
            invoked = true;
            return Task.CompletedTask;
        }));
        Assert.False(invoked);
    }

    private static PlaywrightException NetworkChange() => new("net::ERR_NETWORK_CHANGED at " + Login.AbsoluteUri + "\nCall log:\n  - navigating, waiting until load");

    private sealed class ControlledClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        internal void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }
}
