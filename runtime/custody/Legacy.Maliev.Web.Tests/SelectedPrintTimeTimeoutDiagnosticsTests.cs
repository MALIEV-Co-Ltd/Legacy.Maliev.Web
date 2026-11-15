using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed class SelectedPrintTimeTimeoutDiagnosticsTests
{
    private const string Id = "12345678-1234-1234-1234-123456789abc";

    [Theory]
    [InlineData(0, "ABS", 1, true, true, false)] // Stale pre-change authorization.
    [InlineData(1, "ABS", 1, false, true, false)] // Initial Put has no authority yet.
    [InlineData(1, "PLA", 1, true, true, false)]
    [InlineData(1, "ABS", 2, true, true, false)]
    [InlineData(1, "ABS", 1, true, false, false)]
    [InlineData(1, "ABS", 1, true, true, true)]
    public void ProtectedCompletionRequiresChangedSamePartAbsQuantityOneAndFinalAuthority(
        int seconds, string material, int quantity, bool authorized, bool samePart, bool expected)
    {
        var before = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var expectedPart = Guid.Parse(Id);
        // Geometry is deliberately unused: this tests synchronization, not manufacturing admission.
        var part = new InstantQuotationPart(samePart ? expectedPart : Guid.NewGuid(), "synthetic.stl",
            new InstantQuotationUploadReference("synthetic"), null!,
            new InstantQuotationPartConfiguration(material, "White", quantity));
        var current = new InstantQuotationSessionState("synthetic-session", "synthetic-submission",
            new InstantQuotationOrderState([part]), before, before.AddSeconds(seconds),
            authorized ? new InstantQuotationQuoteAuthorization([], "synthetic-not-read") : null);
        Assert.Equal(expected, SelectedPrintTimeTimeoutDiagnostics.IsMaterialChangeComplete(current, before, expectedPart));
    }

    [Fact]
    public void MissingProtectedSessionCannotSatisfyCompletion() =>
        Assert.False(SelectedPrintTimeTimeoutDiagnostics.IsMaterialChangeComplete(null, DateTimeOffset.UnixEpoch, Guid.Parse(Id)));

    [Fact]
    public async Task ValidObservationAttachesOnlyBoundedTypedFields()
    {
        var original = new TimeoutException("Original selected duration timeout.");
        await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, 3, Id, () => Task.FromResult(
            """{"actualPartId":"12345678-1234-1234-1234-123456789abc","configurationPartId":"12345678-1234-1234-1234-123456789abc","workflow":"configured","isRepricing":false,"material":"ABS","quantity":"1","durationPresent":true,"unavailablePresent":false,"private":"synthetic-ticket-private"}"""));
        Assert.Equal("iteration=3;expectedPart=12345678123412341234123456789abc;actualPart=12345678123412341234123456789abc;configurationPart=12345678123412341234123456789abc;workflow=Configured;isRepricing=False;material=ABS;visibleQuantity=1;durationPresent=True;unavailablePresent=False", original.Data[SelectedPrintTimeTimeoutDiagnostics.DataKey]);
        Assert.Equal("Original selected duration timeout.", original.Message);
    }

    [Fact]
    public async Task PrivateMalformedValuesAreNeverCopiedIntoDiagnostics()
    {
        var original = new TimeoutException();
        await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, 99, "synthetic-ticket-private", () => Task.FromResult(
            """{"actualPartId":"synthetic-cookie-private","configurationPartId":"synthetic-query-private","workflow":"synthetic-identity-private","material":"synthetic-bearer-private","quantity":"10001","durationPresent":"synthetic-private","unavailablePresent":false}"""));
        Assert.Equal("iteration=unknown;expectedPart=unknown;actualPart=unknown;configurationPart=unknown;workflow=Unknown;isRepricing=unknown;material=Unknown;visibleQuantity=unknown;durationPresent=unknown;unavailablePresent=False", original.Data[SelectedPrintTimeTimeoutDiagnostics.DataKey]);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"workflow\":null}")]
    public async Task MalformedOrMissingObservationPreservesOriginal(string json)
    {
        var original = new TimeoutException("Original selected duration timeout.");
        await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, 1, Id, () => Task.FromResult(json));
        Assert.Equal("observation=unavailable", original.Data[SelectedPrintTimeTimeoutDiagnostics.DataKey]);
        Assert.Equal("Original selected duration timeout.", original.Message);
    }

    [Fact]
    public async Task OversizedObservationIsUnavailableRatherThanTruncatedOrLogged()
    {
        var original = new TimeoutException();
        await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, 1, Id, () => Task.FromResult(new string('x', 1025)));
        Assert.Equal("observation=unavailable", original.Data[SelectedPrintTimeTimeoutDiagnostics.DataKey]);
    }

    [Fact]
    public async Task SecondaryObserverFailureCannotReplaceOriginal()
    {
        var original = new TimeoutException("Original selected duration timeout.");
        await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, 1, Id, () => throw new InvalidOperationException("synthetic-private"));
        Assert.Equal("observation=unavailable", original.Data[SelectedPrintTimeTimeoutDiagnostics.DataKey]);
        Assert.Equal("Original selected duration timeout.", original.Message);
        Assert.Null(original.InnerException);
    }

    [Fact]
    public async Task SecondaryObserverCancellationCannotReplaceOriginal()
    {
        var original = new TimeoutException("Original selected duration timeout.");
        await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, 1, Id, () => Task.FromCanceled<string>(new CancellationToken(true)));
        Assert.Equal("observation=unavailable", original.Data[SelectedPrintTimeTimeoutDiagnostics.DataKey]);
        Assert.Equal("Original selected duration timeout.", original.Message);
    }

    [Fact]
    public async Task NoncooperativeSecondaryObservationCannotExtendDiagnosticBudgetIndefinitely()
    {
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new TimeoutException("Original selected duration timeout.");
        try
        {
            await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, 1, Id, () => gate.Task)
                .WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("observation=unavailable", original.Data[SelectedPrintTimeTimeoutDiagnostics.DataKey]);
            Assert.Equal("Original selected duration timeout.", original.Message);
        }
        finally
        {
            gate.TrySetResult("{}");
            await gate.Task;
        }
    }

    [Fact]
    public async Task SafeSinkEmitsExactlyOneReviewedDescription()
    {
        var emitted = new List<string>();
        await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(new TimeoutException(), 1, Id,
            () => Task.FromResult("""{"workflow":"configured","private":"synthetic-ticket-private"}"""), emitted.Add);
        Assert.Equal("iteration=1;expectedPart=12345678123412341234123456789abc;actualPart=unknown;configurationPart=unknown;workflow=Configured;isRepricing=unknown;material=Unknown;visibleQuantity=unknown;durationPresent=unknown;unavailablePresent=unknown", Assert.Single(emitted));
    }

    [Fact]
    public async Task FailingSinkPreservesOriginalInstanceAndBareRethrowSite()
    {
        var original = new TimeoutException("Original selected duration timeout.");
        var attempts = 0;
        async Task ThrowOriginalAsync()
        {
            try { throw original; }
            catch (TimeoutException caught)
            {
                await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(caught, 1, Id,
                    () => Task.FromResult("""{"workflow":"configured"}"""), _ =>
                    {
                        attempts++;
                        throw new InvalidOperationException("synthetic-private");
                    });
                throw;
            }
        }
        var actual = await Record.ExceptionAsync(ThrowOriginalAsync);
        Assert.Same(original, actual);
        Assert.Equal(1, attempts);
        Assert.Contains(nameof(ThrowOriginalAsync), original.StackTrace, StringComparison.Ordinal);
    }
}
