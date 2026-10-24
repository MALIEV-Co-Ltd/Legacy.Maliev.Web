using System.Text.Json;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actor-free controls for passive server milestones; no browser, application host or network.</summary>
public sealed class MaterialInitialRequestStagesTests
{
    [Fact]
    public async Task ObserverReturnsSameResultOnceAndDoesNotRecordOperationValues()
    {
        var stages = new MaterialInitialRequestStages();
        var expected = new object();
        var calls = 0;
        var result = await stages.ObserveAsync(MaterialInitialRequestStages.Boundary.Countries,
            () => { calls++; return Task.FromResult(expected); });

        Assert.Same(expected, result);
        Assert.Equal(1, calls);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(stages.Snapshot()));
        var rows = document.RootElement.GetProperty("Rows");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("entered", rows[0].GetProperty("Outcome").GetString());
        Assert.Equal("returned", rows[1].GetProperty("Outcome").GetString());
        Assert.True(rows[1].GetProperty("ElapsedMilliseconds").GetInt64()
            >= rows[0].GetProperty("ElapsedMilliseconds").GetInt64());
    }

    [Fact]
    public async Task ObserverRethrowsSameFailureOrCancellationWithoutReadingExceptionData()
    {
        foreach (var primary in new Exception[] { new IOException("private-error"), new OperationCanceledException("private-cancel") })
        {
            var stages = new MaterialInitialRequestStages();
            var actual = await Assert.ThrowsAnyAsync<Exception>(() => stages.ObserveAsync<object>(
                MaterialInitialRequestStages.Boundary.WorkflowInitialization,
                () => Task.FromException<object>(primary)));
            Assert.Same(primary, actual);
            var evidence = JsonSerializer.Serialize(stages.Snapshot());
            Assert.DoesNotContain("private", evidence, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(evidence);
            Assert.Equal(primary is OperationCanceledException ? "cancelled" : "faulted",
                document.RootElement.GetProperty("Rows")[1].GetProperty("Outcome").GetString());
        }
    }

    [Fact]
    public void EvidenceIsBoundedFrozenAndRejectsNonVocabularyValues()
    {
        var stages = new MaterialInitialRequestStages();
        stages.Record((MaterialInitialRequestStages.Boundary)999, "private-stage");
        stages.Record(MaterialInitialRequestStages.Boundary.Countries, "private-outcome");
        for (var index = 0; index < 1000; index++)
            stages.Record(MaterialInitialRequestStages.Boundary.Countries, "observed");
        stages.Freeze();
        var frozen = JsonSerializer.Serialize(stages.Snapshot());
        stages.Record(MaterialInitialRequestStages.Boundary.StoreCreate, "entered");

        Assert.Equal(frozen, JsonSerializer.Serialize(stages.Snapshot()));
        Assert.DoesNotContain("private", frozen, StringComparison.Ordinal);
        Assert.True(frozen.Length < 16_384);
        using var document = JsonDocument.Parse(frozen);
        Assert.Equal(64, document.RootElement.GetProperty("Rows").GetArrayLength());
        Assert.Equal(936, document.RootElement.GetProperty("Omitted").GetInt32());
        Assert.True(document.RootElement.GetProperty("ObserverFaulted").GetBoolean());
        Assert.True(document.RootElement.GetProperty("Frozen").GetBoolean());
    }

    [Fact]
    public async Task StoreDecoratorForwardsExactIdentityOwnerAndTokenWithoutLoggingThem()
    {
        var stages = new MaterialInitialRequestStages();
        var original = new RecordingStore();
        var observed = new MaterialInitialRequestStages.Store(original, stages);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Null(await observed.GetAsync("private-session", "private-owner", cancellation.Token));
        Assert.Equal(1, original.Calls);
        Assert.Equal("private-session", original.Id);
        Assert.Equal("private-owner", original.Owner);
        Assert.Equal(cancellation.Token, original.Token);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(stages.Snapshot()), StringComparison.Ordinal);
    }

    private sealed class RecordingStore : IInstantQuotationSessionStore
    {
        internal int Calls { get; private set; }
        internal string? Id { get; private set; }
        internal string? Owner { get; private set; }
        internal CancellationToken Token { get; private set; }

        public Task<InstantQuotationSessionState?> GetAsync(string id, string? owner, CancellationToken token)
        {
            Calls++;
            Id = id;
            Owner = owner;
            Token = token;
            return Task.FromResult<InstantQuotationSessionState?>(null);
        }

        public Task<InstantQuotationSessionState> CreateAsync(string? owner, InstantQuotationOrderState request, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? owner, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<bool> RemoveAsync(string id, string? owner, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
