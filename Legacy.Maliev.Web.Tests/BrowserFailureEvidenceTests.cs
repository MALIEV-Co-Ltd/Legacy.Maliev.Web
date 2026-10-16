using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed class BrowserFailureEvidenceTests
{
    [Theory]
    [InlineData("https://example.test/_blazor?id=private", true)]
    [InlineData("ws://example.test/_blazor?id=private", true)]
    [InlineData("https://example.test/_blazor/negotiate?secret=private", true)]
    [InlineData("https://example.test/_blazor/private-file", false)]
    [InlineData("https://example.test/customer?path=/_blazor", false)]
    public void OnlyKnownBlazorPathsAreRecognized(string url, bool expected) =>
        Assert.Equal(expected, BrowserFailureEvidence.IsBlazor(url));

    [Theory]
    [InlineData("private customer Connection disconnected", "disconnect")]
    [InlineData("private timeout detail", "timeout")]
    [InlineData("Connection disconnected: Server timeout elapsed", "timeout")]
    [InlineData("private filename.stl", "other")]
    public void ErrorTextBecomesFixedCategory(string text, string category) =>
        Assert.Equal(category, BrowserFailureEvidence.Category(text));

    [Theory]
    [InlineData("{\"workflow\":\"configured\",\"parts\":33}")]
    [InlineData("{\"workflow\":\"private\"}")]
    [InlineData("{\"workflow\":\"configured\",\"filename\":\"private\"}")]
    [InlineData("{\"workflow\":\"configured\",\"parts\":1,\"parts\":2}")]
    [InlineData("{\"workflow\":\"configured\",\"progress\":\"private\"}")]
    public void ObservationRejectsPrivateOrUnboundedValues(string json) =>
        Assert.Throws<FormatException>(() => BrowserFailureEvidence.ParseObservation(json));

    [Fact]
    public async Task EventsAreBoundedCategoricalAndStageAttributed()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        using var evidence = new BrowserFailureEvidence(page);
        evidence.Stage("Reload");
        for (var i = 0; i < 40; i++) proxy.Raise("PageError", "private filename /ticket Connection disconnected");
        Assert.Throws<ArgumentOutOfRangeException>(() => evidence.Stage("private"));
        var original = new TimeoutException("primary");
        var output = new List<string>();
        await evidence.AttachAsync(original, () => Task.FromResult("{\"workflow\":\"configured\",\"parts\":2,\"uploadRows\":0,\"progress\":false}"), output.Add);
        var json = Assert.Single(output);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(32, document.RootElement.GetProperty("Events").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("Truncated").GetBoolean());
        Assert.All(document.RootElement.GetProperty("Events").EnumerateArray(), item =>
        {
            Assert.Equal("Reload", item.GetProperty("Stage").GetString());
            Assert.Equal("disconnect", item.GetProperty("Category").GetString());
        });
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
        Assert.Equal(json, original.Data[BrowserFailureEvidence.DataKey]);
        Assert.Equal("primary", original.Message);
    }

    [Fact]
    public async Task FailingObserverAndSinkDoNotReplaceOriginal()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        using var evidence = new BrowserFailureEvidence(page);
        var original = new TimeoutException("primary");
        await evidence.AttachAsync(original, () => Task.FromException<string>(new InvalidOperationException("private")),
            _ => throw new InvalidOperationException("private sink"));
        Assert.Contains("unavailable", Assert.IsType<string>(original.Data[BrowserFailureEvidence.DataKey]), StringComparison.Ordinal);
        Assert.Null(original.InnerException);
        Assert.Equal("primary", original.Message);
    }

    [Fact]
    public async Task LateObserverFaultIsObservedAndObservationStopsWithinBudget()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        using var evidence = new BrowserFailureEvidence(page);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new TimeoutException("primary");
        try
        {
            await evidence.AttachAsync(original, () => pending.Task, _ => { }).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains("unavailable", Assert.IsType<string>(original.Data[BrowserFailureEvidence.DataKey]), StringComparison.Ordinal);
        }
        finally
        {
            pending.TrySetException(new InvalidOperationException("late private failure"));
            try { await pending.Task; }
            catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public void DisposeDetachesExactlyOnceIncludingOwnedSocketSubscriptions()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        var socket = DispatchProxy.Create<IWebSocket, EventProxy>();
        var socketProxy = (EventProxy)(object)socket;
        socketProxy.Url = "ws://example.test/_blazor?id=private";
        var evidence = new BrowserFailureEvidence(page);
        proxy.Raise("WebSocket", socket);
        Assert.Equal(6, proxy.Adds);
        Assert.Equal(2, socketProxy.Adds);
        evidence.Dispose();
        evidence.Dispose();
        Assert.Equal(6, proxy.Removes);
        Assert.Equal(2, socketProxy.Removes);
        Assert.Empty(proxy.Handlers);
        Assert.Empty(socketProxy.Handlers);
    }

    [Fact]
    public void SocketSubscriptionsAreBoundedAndIgnoreUnrelatedPaths()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        using var evidence = new BrowserFailureEvidence(page);
        var observed = new List<EventProxy>();
        for (var index = 0; index < 40; index++)
        {
            var socket = DispatchProxy.Create<IWebSocket, EventProxy>();
            var socketProxy = (EventProxy)(object)socket;
            observed.Add(socketProxy);
            proxy.Raise("WebSocket", socket);
        }
        var unrelated = DispatchProxy.Create<IWebSocket, EventProxy>();
        ((EventProxy)(object)unrelated).Url = "wss://example.test/private-file?secret=ticket";
        proxy.Raise("WebSocket", unrelated);
        Assert.Equal(64, observed.Sum(item => item.Adds));
        Assert.Equal(0, ((EventProxy)(object)unrelated).Adds);
        evidence.Dispose();
        Assert.Equal(64, observed.Sum(item => item.Removes));
        Assert.All(observed, item => Assert.Empty(item.Handlers));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorRollsBackAccessorFailureBeforeOrAfterAttachment(bool afterAttachment)
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        proxy.ThrowAdd = "Request";
        proxy.ThrowAfterAdd = afterAttachment;
        var cleanup = new List<string>();
        using var evidence = new BrowserFailureEvidence(page, cleanup.Add);
        Assert.Empty(proxy.Handlers);
        Assert.Equal(3, proxy.Adds);
        Assert.Equal(3, proxy.Removes);
        using var report = JsonDocument.Parse(Assert.Single(cleanup));
        Assert.True(report.RootElement.GetProperty("AcquisitionFailed").GetBoolean());
        Assert.Equal(0, report.RootElement.GetProperty("RemainingSubscriptions").GetInt32());
    }

    [Fact]
    public void ConstructorRetainsFailedRollbackReferenceForDisposeRetry()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        proxy.ThrowAdd = "Request";
        proxy.ThrowAfterAdd = true;
        proxy.ThrowRemove = "Console";
        proxy.RemoveFailures = 1;
        var cleanup = new List<string>();
        using var evidence = new BrowserFailureEvidence(page, cleanup.Add);
        Assert.Single(proxy.Handlers);
        using (var report = JsonDocument.Parse(Assert.Single(cleanup)))
            Assert.Equal(1, report.RootElement.GetProperty("RemainingSubscriptions").GetInt32());
        evidence.Dispose();
        Assert.Empty(proxy.Handlers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SocketPartialAcquisitionRollsBackEachOwnedSubscription(bool afterAttachment)
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var socket = DispatchProxy.Create<IWebSocket, EventProxy>();
        var socketProxy = (EventProxy)(object)socket;
        socketProxy.ThrowAdd = "SocketError";
        socketProxy.ThrowAfterAdd = afterAttachment;
        using var evidence = new BrowserFailureEvidence(page);
        ((EventProxy)(object)page).Raise("WebSocket", socket);
        Assert.Equal(2, socketProxy.Adds);
        Assert.Equal(2, socketProxy.Removes);
        Assert.Empty(socketProxy.Handlers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposeAttemptsOtherRemovalsAndRetriesOnlyResidualReferences(bool detachedBeforeThrow)
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        proxy.ThrowRemove = "Console";
        proxy.RemoveFailures = 1;
        proxy.ThrowAfterRemove = detachedBeforeThrow;
        var cleanup = new List<string>();
        using var evidence = new BrowserFailureEvidence(page, cleanup.Add);
        evidence.Dispose();
        Assert.Equal(6, proxy.Removes);
        if (detachedBeforeThrow) Assert.Empty(proxy.Handlers);
        else Assert.Single(proxy.Handlers);
        using (var report = JsonDocument.Parse(Assert.Single(cleanup)))
            Assert.Equal(1, report.RootElement.GetProperty("RemainingSubscriptions").GetInt32());
        evidence.Dispose();
        Assert.Equal(7, proxy.Removes);
        Assert.Empty(proxy.Handlers);
        proxy.Raise("PageError", "private-late-event");
    }

    [Fact]
    public void UsingCleanupAndSinkFailuresCannotReplacePrimaryException()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        proxy.ThrowRemove = "Console";
        proxy.RemoveFailures = 1;
        var original = new TimeoutException("primary");
        var evidence = new BrowserFailureEvidence(page, _ => throw new InvalidOperationException("private-sink"));
        var actual = Record.Exception((Action)(() =>
        {
            using (evidence) { throw original; }
        }));
        Assert.Same(original, actual);
        evidence.Dispose();
        Assert.Empty(proxy.Handlers);
    }

    [Fact]
    public async Task PageCloseReleasesDelayedObservationAndAsyncCleanupReportsSettlement()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.OnClose = () =>
        {
            pending.TrySetResult("{\"workflow\":\"configured\"}");
            return Task.CompletedTask;
        };
        var cleanup = new List<string>();
        await using var evidence = new BrowserFailureEvidence(page, cleanup.Add);
        try
        {
            await evidence.AttachAsync(new TimeoutException(), () => pending.Task, _ => { });
            Assert.False(pending.Task.IsCompleted);
            await evidence.DisposeAsync();
            Assert.True(pending.Task.IsCompletedSuccessfully);
            Assert.Equal(1, proxy.CloseCalls);
            using var report = JsonDocument.Parse(cleanup.Last());
            Assert.Equal("complete", report.RootElement.GetProperty("State").GetString());
            Assert.Equal("closed", report.RootElement.GetProperty("PageClose").GetString());
            Assert.Equal(0, report.RootElement.GetProperty("PendingObservations").GetInt32());
        }
        finally
        {
            pending.TrySetResult("{\"workflow\":\"empty\"}");
            await pending.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task UnresolvedObservationIsReportedAndRetainedUntilExplicitlySettled()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new List<string>();
        var original = new TimeoutException("primary");
        BrowserFailureEvidence? recovery = null;
        try
        {
            await LeaveEvidenceScopeAsync();
            recovery = Assert.IsType<BrowserFailureEvidence>(original.Data[BrowserFailureEvidence.RecoveryKey]);
            Assert.False(pending.Task.IsCompleted);
            using var report = JsonDocument.Parse(cleanup.Last());
            Assert.Equal("unsettled", report.RootElement.GetProperty("State").GetString());
            Assert.Equal(1, report.RootElement.GetProperty("PendingObservations").GetInt32());
            Assert.Equal("exception-owned", report.RootElement.GetProperty("Recovery").GetString());
        }
        finally
        {
            pending.TrySetException(new InvalidOperationException("private-late-fault"));
            try { await pending.Task.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (InvalidOperationException) { }
            recovery ??= original.Data[BrowserFailureEvidence.RecoveryKey] as BrowserFailureEvidence;
            if (recovery is not null) await recovery.DisposeAsync();
        }
        using var settled = JsonDocument.Parse(cleanup.Last());
        Assert.Equal(0, settled.RootElement.GetProperty("PendingObservations").GetInt32());
        Assert.False(original.Data.Contains(BrowserFailureEvidence.RecoveryKey));
        Assert.DoesNotContain("private-", string.Join('\n', cleanup), StringComparison.Ordinal);

        async Task LeaveEvidenceScopeAsync()
        {
            await using var evidence = new BrowserFailureEvidence(page, cleanup.Add);
            await evidence.AttachAsync(original, () => pending.Task, _ => { });
        }
    }

    [Fact]
    public async Task AsyncCloseFailureCannotReplacePrimaryException()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        proxy.OnClose = () => Task.FromException(new InvalidOperationException("private-close-fault"));
        var cleanup = new List<string>();
        var original = new TimeoutException("primary");
        var actual = await Record.ExceptionAsync(async () =>
        {
            await using var evidence = new BrowserFailureEvidence(page, json =>
            {
                cleanup.Add(json);
                throw new InvalidOperationException("private-cleanup-sink-fault");
            });
            throw original;
        });
        Assert.Same(original, actual);
        using var report = JsonDocument.Parse(cleanup.Last());
        Assert.Equal("failed", report.RootElement.GetProperty("PageClose").GetString());
        Assert.Equal("unsettled", report.RootElement.GetProperty("State").GetString());
        Assert.Empty(proxy.Handlers);
    }

    [Fact]
    public async Task PendingPageCloseRetainsSameTaskAndReportsUnsettledUntilReleased()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.OnClose = () => close.Task;
        var cleanup = new List<string>();
        var original = new TimeoutException("primary");
        await using var evidence = new BrowserFailureEvidence(page, cleanup.Add);
        try
        {
            await evidence.AttachAsync(original, () => Task.FromResult("{\"workflow\":\"configured\"}"), _ => { });
            await evidence.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
            Assert.False(close.Task.IsCompleted);
            Assert.Equal(1, proxy.CloseCalls);
            using var report = JsonDocument.Parse(cleanup.Last());
            Assert.Equal("pending", report.RootElement.GetProperty("PageClose").GetString());
            Assert.Equal("unsettled", report.RootElement.GetProperty("State").GetString());
            Assert.Same(evidence, original.Data[BrowserFailureEvidence.RecoveryKey]);
        }
        finally
        {
            close.TrySetResult();
            await close.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await evidence.DisposeAsync();
        }
        Assert.Equal(1, proxy.CloseCalls);
        using var settled = JsonDocument.Parse(cleanup.Last());
        Assert.Equal("complete", settled.RootElement.GetProperty("State").GetString());
        Assert.False(original.Data.Contains(BrowserFailureEvidence.RecoveryKey));
    }

    [Fact]
    public async Task OriginalFailureOwnsFailedUndoAfterLexicalScopeAndRetryReleasesIt()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        proxy.ThrowRemove = "Console";
        proxy.RemoveFailures = 2;
        var original = new TimeoutException("primary");
        var actual = await Record.ExceptionAsync(async () =>
        {
            await using var evidence = new BrowserFailureEvidence(page);
            await evidence.AttachAsync(original, () => Task.FromResult("{\"workflow\":\"configured\"}"), _ => { });
            throw original;
        });
        Assert.Same(original, actual);
        var recovery = Assert.IsType<BrowserFailureEvidence>(original.Data[BrowserFailureEvidence.RecoveryKey]);
        Assert.Single(proxy.Handlers);
        recovery.Dispose();
        Assert.Empty(proxy.Handlers);
        Assert.False(original.Data.Contains(BrowserFailureEvidence.RecoveryKey));
    }

    [Fact]
    public void SocketFailedRollbackRetainsExactUndoForDisposeRetry()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var socket = DispatchProxy.Create<IWebSocket, EventProxy>();
        var socketProxy = (EventProxy)(object)socket;
        socketProxy.ThrowAdd = "SocketError";
        socketProxy.ThrowAfterAdd = true;
        socketProxy.ThrowRemove = "Close";
        socketProxy.RemoveFailures = 1;
        var cleanup = new List<string>();
        using var evidence = new BrowserFailureEvidence(page, cleanup.Add);
        ((EventProxy)(object)page).Raise("WebSocket", socket);
        Assert.Single(socketProxy.Handlers);
        Assert.Equal(2, socketProxy.Removes);
        using (var report = JsonDocument.Parse(cleanup.Last()))
            Assert.Equal(7, report.RootElement.GetProperty("RemainingSubscriptions").GetInt32());
        evidence.Dispose();
        Assert.Equal(3, socketProxy.Removes);
        Assert.Empty(socketProxy.Handlers);
        using var settled = JsonDocument.Parse(cleanup.Last());
        Assert.Equal(0, settled.RootElement.GetProperty("RemainingSubscriptions").GetInt32());
    }

    [Fact]
    public async Task SynchronousUndoRetryKeepsRecoveryUntilConfirmedPageClose()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        proxy.ThrowRemove = "Console";
        proxy.RemoveFailures = 1;
        var original = new TimeoutException("primary");
        await using var evidence = new BrowserFailureEvidence(page);
        await evidence.AttachAsync(original, () => Task.FromResult("{\"workflow\":\"configured\"}"), _ => { });
        evidence.Dispose();
        Assert.Same(evidence, original.Data[BrowserFailureEvidence.RecoveryKey]);
        evidence.Dispose();
        Assert.Empty(proxy.Handlers);
        Assert.Equal(0, proxy.CloseCalls);
        Assert.Same(evidence, original.Data[BrowserFailureEvidence.RecoveryKey]);
        await evidence.DisposeAsync();
        Assert.Equal(1, proxy.CloseCalls);
        Assert.False(original.Data.Contains(BrowserFailureEvidence.RecoveryKey));
    }

    [Theory]
    [InlineData("[2026-10-12T12:12:22.981Z] private filename /ticket timeout", true)]
    [InlineData("[2026-13-12T12:12:22.981Z] private filename /ticket timeout", false)]
    [InlineData("private filename /ticket timeout", false)]
    public async Task ConsoleTimingRetainsOnlyValidatedUtcPrefixAndCategoricalDelivery(string text, bool valid)
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var message = DispatchProxy.Create<IConsoleMessage, EventProxy>();
        ((EventProxy)(object)message).Text = text;
        using var evidence = new BrowserFailureEvidence(page);
        ((EventProxy)(object)page).Raise("Console", message);
        var output = new List<string>();
        await evidence.AttachAsync(new TimeoutException("primary"),
            () => Task.FromResult("{\"workflow\":\"configured\"}"), output.Add);
        var json = Assert.Single(output);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("managed-callback-arrival", root.GetProperty("ClockDomain").GetString());
        Assert.Equal(TimeSpan.Zero, root.GetProperty("ClockOriginUtc").GetDateTimeOffset().Offset);
        var item = Assert.Single(root.GetProperty("Events").EnumerateArray());
        Assert.Equal("timeout", item.GetProperty("Category").GetString());
        Assert.Equal(TimeSpan.Zero, item.GetProperty("CallbackUtc").GetDateTimeOffset().Offset);
        Assert.True(item.GetProperty("Milliseconds").GetInt64() >= 0);
        var emitted = item.GetProperty("BrowserEmittedUtc");
        if (valid)
            Assert.Equal(new DateTimeOffset(2026, 10, 12, 12, 12, 22, TimeSpan.Zero).AddMilliseconds(981),
                emitted.GetDateTimeOffset());
        else
            Assert.Equal(JsonValueKind.Null, emitted.ValueKind);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/ticket", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AwaitMarkersRejectUnknownNamesAndClearCompletedOperation()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        using var evidence = new BrowserFailureEvidence(page);
        evidence.AwaitBoundary("private customer filename", false);
        proxy.Raise("PageError", "private disconnect");
        evidence.AwaitBoundary("first-keyboard", false);
        proxy.Raise("PageError", "private disconnect");
        evidence.AwaitBoundary("first-keyboard", true);
        proxy.Raise("PageError", "private disconnect");
        var output = new List<string>();
        await evidence.AttachAsync(new TimeoutException("primary"),
            () => Task.FromResult("{\"workflow\":\"configured\"}"), output.Add);
        var json = Assert.Single(output);
        using var document = JsonDocument.Parse(json);
        var events = document.RootElement.GetProperty("Events").EnumerateArray().ToArray();
        Assert.Equal(JsonValueKind.Null, events[0].GetProperty("ActiveAwait").ValueKind);
        Assert.Equal("first-keyboard", events[1].GetProperty("ActiveAwait").GetString());
        Assert.Equal(JsonValueKind.Null, events[2].GetProperty("ActiveAwait").ValueKind);
        var markers = document.RootElement.GetProperty("AwaitEvents").EnumerateArray().ToArray();
        Assert.Equal(2, markers.Length);
        Assert.All(markers, item => Assert.Equal("first-keyboard", item.GetProperty("Operation").GetString()));
        Assert.False(markers[0].GetProperty("Completed").GetBoolean());
        Assert.True(markers[1].GetProperty("Completed").GetBoolean());
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AwaitMarkersHaveIndependentFixedCapWithoutWeakeningEventLimit()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        using var evidence = new BrowserFailureEvidence(page);
        for (var index = 0; index < 20; index++)
        {
            evidence.AwaitBoundary("initial-screenshot", false);
            evidence.AwaitBoundary("initial-screenshot", true);
        }
        var proxy = (EventProxy)(object)page;
        for (var index = 0; index < 40; index++) proxy.Raise("PageError", "private disconnect");
        var output = new List<string>();
        await evidence.AttachAsync(new TimeoutException("primary"),
            () => Task.FromResult("{\"workflow\":\"configured\"}"), output.Add);
        using var document = JsonDocument.Parse(Assert.Single(output));
        Assert.Equal(32, document.RootElement.GetProperty("AwaitEvents").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("AwaitTruncated").GetBoolean());
        Assert.Equal(32, document.RootElement.GetProperty("Events").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("Truncated").GetBoolean());
    }

    [Fact]
    public void FinishingNavigation_OnlyFixedPublicCategoriesLeaveTheCollector()
    {
        var origin = new Uri("http://127.0.0.1:1234");
        Assert.Equal("/services/finishing-and-color", FinishingNavigationEvidence.Classify(origin,
            "http://127.0.0.1:1234/services/finishing-and-color?private=secret"));
        Assert.Equal("/dist/site.min.css", FinishingNavigationEvidence.Classify(origin,
            "http://127.0.0.1:1234/dist/site.min.css?private=secret"));
        Assert.Equal("<other>", FinishingNavigationEvidence.Classify(origin,
            "http://127.0.0.1:1234/customer/private-file?token=secret"));
        Assert.Equal("<third-party>", FinishingNavigationEvidence.Classify(origin,
            "https://external.invalid/private-file?token=secret"));
        Assert.Equal("<other>", FinishingNavigationEvidence.Classify(origin, "not a private URI"));
    }

    [Fact]
    public void FinishingNavigation_RequestStorageCountersAndOutputAreBounded()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        using var evidence = new FinishingNavigationEvidence(page, new Uri("http://127.0.0.1:1234"));
        for (var index = 0; index < 10020; index++)
        {
            evidence.RecordRequest(new object(), "private-type", "http://127.0.0.1:1234/private?token=secret", false);
        }
        var output = new List<string>();
        evidence.Report(output.Add);
        var json = Assert.Single(output);
        Assert.True(json.Length <= 8192);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(32, document.RootElement.GetProperty("Pending").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("Truncated").GetBoolean());
        Assert.Equal(9999, document.RootElement.GetProperty("Started").GetInt32());
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("?", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FinishingNavigation_LifecycleAndFaultsPreservePrimaryAndDetachDelegates()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        var evidence = new FinishingNavigationEvidence(page, new Uri("http://127.0.0.1:1234"));
        proxy.Raise("DOMContentLoaded", page);
        proxy.Raise("Load", page);
        var output = new List<string>();
        evidence.Report(output.Add);
        using (var document = JsonDocument.Parse(Assert.Single(output)))
        {
            Assert.True(document.RootElement.GetProperty("DomContentLoaded").GetBoolean());
            Assert.True(document.RootElement.GetProperty("Loaded").GetBoolean());
        }
        proxy.ThrowRemove = "Request";
        proxy.RemoveFailures = 1;
        var original = new TimeoutException("primary");
        var actual = Record.Exception((Action)(() =>
        {
            try { throw original; }
            catch
            {
                evidence.Report(_ => throw new InvalidOperationException("private-sink"));
                throw;
            }
            finally { evidence.Dispose(); }
        }));
        Assert.Same(original, actual);
        evidence.Dispose();
        Assert.Empty(proxy.Handlers);

        var failedPage = DispatchProxy.Create<IPage, EventProxy>();
        var failedProxy = (EventProxy)(object)failedPage;
        failedProxy.ThrowAdd = "Response";
        failedProxy.ThrowAfterAdd = true;
        using var failed = new FinishingNavigationEvidence(failedPage, new Uri("http://127.0.0.1:1234"));
        Assert.Empty(failedProxy.Handlers);
    }

    [Fact]
    public void FinishingNavigation_NewMainRequestCannotReuseThePreviousResponseOrCompletion()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        using var evidence = new FinishingNavigationEvidence(page, new Uri("http://127.0.0.1:1234"));
        var output = new List<string>();
        evidence.Report(output.Add);
        using (var beforeRequest = JsonDocument.Parse(output[^1]))
        {
            Assert.Equal(JsonValueKind.Null, beforeRequest.RootElement.GetProperty("MainResponseStatus").ValueKind);
            Assert.Equal(JsonValueKind.Null, beforeRequest.RootElement.GetProperty("MainResponseElapsedMs").ValueKind);
            Assert.False(beforeRequest.RootElement.GetProperty("MainDocumentFinished").GetBoolean());
        }
        var first = DispatchProxy.Create<IRequest, EventProxy>();
        evidence.RecordRequest(first, "document", "http://127.0.0.1:1234/services/finishing-and-color", true);
        var response = DispatchProxy.Create<IResponse, EventProxy>();
        var responseProxy = (EventProxy)(object)response;
        responseProxy.ResponseRequest = first;
        responseProxy.ResponseStatus = 302;
        proxy.Raise("Response", response);
        proxy.Raise("RequestFinished", first);
        evidence.Report(output.Add);
        using (var firstResponse = JsonDocument.Parse(output[^1]))
        {
            Assert.Equal(302, firstResponse.RootElement.GetProperty("MainResponseStatus").GetInt32());
            Assert.Equal(JsonValueKind.Number, firstResponse.RootElement.GetProperty("MainResponseElapsedMs").ValueKind);
            Assert.True(firstResponse.RootElement.GetProperty("MainDocumentFinished").GetBoolean());
        }
        var second = DispatchProxy.Create<IRequest, EventProxy>();
        evidence.RecordRequest(second, "document", "http://127.0.0.1:1234/services/finishing-and-color", true);
        // A late old-generation response/completion must not repopulate the new generation.
        proxy.Raise("Response", response);
        proxy.Raise("RequestFinished", first);
        evidence.Report(output.Add);
        using var secondRequest = JsonDocument.Parse(output[^1]);
        Assert.Equal(JsonValueKind.Null, secondRequest.RootElement.GetProperty("MainResponseStatus").ValueKind);
        Assert.Equal(JsonValueKind.Null, secondRequest.RootElement.GetProperty("MainResponseElapsedMs").ValueKind);
        Assert.False(secondRequest.RootElement.GetProperty("MainDocumentFinished").GetBoolean());
    }

    // No browser or SDK process is created: only interface event accessors are emulated.
    public class EventProxy : DispatchProxy
    {
        internal readonly Dictionary<string, Delegate> Handlers = [];
        internal int Adds;
        internal int Removes;
        internal string Url = "https://example.test/_blazor";
        internal string Text = "private timeout";
        internal string Type = "error";
        internal string? ThrowAdd;
        internal bool ThrowAfterAdd;
        internal string? ThrowRemove;
        internal int RemoveFailures;
        internal bool ThrowAfterRemove;
        internal Func<Task> OnClose = () => Task.CompletedTask;
        internal int CloseCalls;
        internal IRequest? ResponseRequest;
        internal int ResponseStatus = 200;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name.StartsWith("add_", StringComparison.Ordinal))
            {
                Adds++;
                var key = name[4..];
                if (key == ThrowAdd && !ThrowAfterAdd) throw new InvalidOperationException("private-add-fault");
                Handlers[key] = Handlers.TryGetValue(key, out var old) ? Delegate.Combine(old, (Delegate)args![0]!) : (Delegate)args![0]!;
                if (key == ThrowAdd) throw new InvalidOperationException("private-add-fault");
                return null;
            }
            if (name.StartsWith("remove_", StringComparison.Ordinal))
            {
                Removes++;
                var key = name[7..];
                var fail = key == ThrowRemove && RemoveFailures-- > 0;
                if (fail && !ThrowAfterRemove) throw new InvalidOperationException("private-remove-fault");
                if (Handlers.TryGetValue(key, out var old))
                {
                    var remaining = Delegate.Remove(old, (Delegate)args![0]!);
                    if (remaining is null) Handlers.Remove(key); else Handlers[key] = remaining;
                }
                if (fail) throw new InvalidOperationException("private-remove-fault");
                return null;
            }
            if (name == "get_Request") return ResponseRequest;
            if (name == "get_Status") return ResponseStatus;
            if (name == "get_Url") return Url;
            if (name == "get_Text") return Text;
            if (name == "get_Type") return Type;
            if (name == "CloseAsync")
            {
                CloseCalls++;
                return OnClose();
            }
            throw new NotSupportedException(name);
        }
        internal void Raise<T>(string name, T value)
        {
            if (Handlers.TryGetValue(name, out var handler)) handler.DynamicInvoke(this, value);
        }
    }
}
