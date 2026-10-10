using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Legacy.Maliev.Web.Tests;

public sealed class SummaryServerNavigationEvidenceTests
{
    [Fact]
    public async Task PendingDocumentDistinguishesServerEntryBeforeAnyResponse()
    {
        var evidence = new SummaryServerNavigationEvidence();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = Document();
        var operation = evidence.ObserveAsync(context, _ => release.Task);
        try
        {
            Assert.Equal(new(1, 0, 0, 0, 0, 1), evidence.Snapshot());
        }
        finally
        {
            release.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(new(1, 0, 1, 0, 0, 0), evidence.Snapshot());
    }

    [Fact]
    public async Task RealServerResponseStartIsObservedWithoutRetainingRequestData()
    {
        var evidence = new SummaryServerNavigationEvidence();
        using var server = new TestServer(new WebHostBuilder().Configure(app =>
        {
            app.Use(evidence.ObserveAsync);
            app.Run(context => context.Response.WriteAsync("fixed"));
        }));
        using var client = server.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        using var response = await client.GetAsync("/instantquotation/3d-printing?token=private-token");
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(new(1, 1, 1, 0, 0, 0), evidence.Snapshot());
        Assert.DoesNotContain("private", JsonSerializer.Serialize(evidence.Snapshot()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FaultRetainsOriginalExceptionAndSettlesPendingCount()
    {
        var evidence = new SummaryServerNavigationEvidence();
        var original = new InvalidOperationException("private-exception");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            evidence.ObserveAsync(Document(), _ => Task.FromException(original)));
        Assert.Same(original, actual);
        Assert.Equal(new(1, 0, 0, 1, 0, 0), evidence.Snapshot());
        Assert.DoesNotContain("private", JsonSerializer.Serialize(evidence.Snapshot()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbortedRequestIsCountedWithoutRetainingItsToken()
    {
        var evidence = new SummaryServerNavigationEvidence();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = Document();
        context.RequestAborted = cancellation.Token;
        await evidence.ObserveAsync(context, _ => Task.CompletedTask);
        Assert.Equal(new(1, 0, 1, 0, 1, 0), evidence.Snapshot());
    }

    [Theory]
    [InlineData("GET", "/private-token")]
    [InlineData("POST", "/instantquotation/3d-printing")]
    public async Task UnrelatedRequestsRemainUnobserved(string method, string path)
    {
        var evidence = new SummaryServerNavigationEvidence();
        var context = Document();
        context.Request.Method = method;
        context.Request.Path = path;
        var calls = 0;
        await evidence.ObserveAsync(context, _ => { calls++; return Task.CompletedTask; });
        Assert.Equal(1, calls);
        Assert.Equal(new(0, 0, 0, 0, 0, 0), evidence.Snapshot());
    }

    [Fact]
    public void ObservationOutputCannotReplacePrimaryFailure()
    {
        SummaryServerNavigationEvidence.PreserveFailure(() => throw new InvalidOperationException("private"));
        var snapshot = SummaryServerNavigationEvidence.RunnerSnapshot();
        Assert.InRange(snapshot.ManagedBytes, 0, 1L << 40);
        Assert.InRange(snapshot.GcAvailableBytes, 0, 1L << 40);
        Assert.InRange(snapshot.ThreadPoolThreads, 0, 65536);
        Assert.InRange(snapshot.PendingWorkItems, 0, 1L << 40);
    }

    private static DefaultHttpContext Document()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/instantquotation/3d-printing";
        return context;
    }
}
