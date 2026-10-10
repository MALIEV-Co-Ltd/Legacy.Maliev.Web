using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationNavigationFailureDiagnosticsTests
{
    [Theory]
    [InlineData("http://127.0.0.1:46767/_blazor?id=secret#secret", "/_blazor")]
    [InlineData("http://127.0.0.1:46767/instantquotation/3d-printing?culture=th&token=secret", "/instantquotation/3d-printing")]
    [InlineData("http://127.0.0.1:46767/dist/app.min.js?v=secret", "/dist/app.min.js")]
    [InlineData("http://127.0.0.1:46767/services/3d-printing?culture=en&token=secret#secret", "/services/3d-printing")]
    [InlineData("http://127.0.0.1:46767/services/3d-printing/secret", "same-origin-other")]
    [InlineData("http://127.0.0.1:46767/customer/secret", "same-origin-other")]
    [InlineData("http://127.0.0.1:46767/dist/secret.js", "same-origin-other")]
    [InlineData("http://secret@127.0.0.1:46767/_blazor", "cross-origin")]
    [InlineData("http://127.0.0.1:46768/_blazor?secret", "cross-origin")]
    [InlineData("https://external.invalid/secret", "cross-origin")]
    [InlineData("secret", "invalid")]
    public void PathsExcludeQueriesCredentialsAndUnknownContent(string url, string expected) =>
        Assert.Equal(expected, NavigationRequestLedger.SafePath(new("http://127.0.0.1:46767"), url));

    [Fact]
    public void CapRetainsTrackedTerminalStateWithoutRetainingUntrustedText()
    {
        var ledger = new NavigationRequestLedger(new("http://127.0.0.1:46767"));
        var first = new object();
        ledger.Start(first, "http://127.0.0.1:46767/_blazor?id=secret", "GET", "fetch");
        for (var index = 0; index < 199; index++)
            ledger.Start(new object(), "http://127.0.0.1:46767/customer/secret", "secret", "secret");
        ledger.Respond(first, 503);
        ledger.End(first, "failed", "message containing secret");
        var snapshot = ledger.Snapshot();
        Assert.Equal(200, snapshot.Observed);
        Assert.Equal(168, snapshot.Omitted);
        Assert.Equal(32, snapshot.Requests.Length);
        Assert.Equal(503, snapshot.Requests[0].Status);
        Assert.Equal("failed", snapshot.Requests[0].Outcome);
        Assert.Equal("other", snapshot.Requests[0].FailureCode);
        Assert.NotNull(snapshot.Requests[0].EndedMs);
        var retained = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("secret", retained, StringComparison.Ordinal);
        Assert.True(retained.Length < 8192);
    }

    [Theory]
    [InlineData("net::ERR_CONNECTION_REFUSED", "net::ERR_CONNECTION_REFUSED")]
    [InlineData("net::ERR_TIMED_OUT", "net::ERR_TIMED_OUT")]
    [InlineData("net::ERR_FAILED http://example.invalid/secret", "other")]
    [InlineData(null, "unspecified")]
    public void FailureCodesExcludeRawMessages(string? failure, string expected) =>
        Assert.Equal(expected, NavigationRequestLedger.SafeFailureCode(failure));

    [Fact]
    public void FailureTimeSnapshotDoesNotChangeWhenTheRequestLaterCompletes()
    {
        var ledger = new NavigationRequestLedger(new("http://127.0.0.1:46767"));
        var request = new object();
        ledger.Start(request, "http://127.0.0.1:46767/_blazor", "GET", "fetch");
        var atFailure = Assert.Single(ledger.Snapshot().Requests);
        ledger.Respond(request, 200);
        ledger.End(request, "finished");
        Assert.Equal("pending", atFailure.Outcome);
        Assert.Null(atFailure.Status);
        Assert.Null(atFailure.EndedMs);
        var completed = Assert.Single(ledger.Snapshot().Requests);
        Assert.Equal("finished", completed.Outcome);
        Assert.Equal(200, completed.Status);
        Assert.NotNull(completed.EndedMs);
    }

    [Fact]
    public async Task DiagnosticFailurePreservesTheOriginalExceptionInstance()
    {
        var original = new TimeoutException("original navigation failure");
        var actual = await Assert.ThrowsAsync<TimeoutException>(() =>
            InstantQuotationNavigationFailureDiagnostics.PreserveFailureAsync(
                () => Task.FromException<int>(original), () => Task.FromException(new InvalidOperationException("diagnostic failure"))));
        Assert.Same(original, actual);
    }

    [Fact]
    public async Task SuccessfulNavigationDoesNotRunDiagnostics()
    {
        var calls = 0;
        var actual = await InstantQuotationNavigationFailureDiagnostics.PreserveFailureAsync(
            () => Task.FromResult(200), () => { calls++; return Task.CompletedTask; });
        Assert.Equal(200, actual);
        Assert.Equal(0, calls);
    }
}
