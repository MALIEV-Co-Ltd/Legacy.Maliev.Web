using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationQuantityEditStateTests
{
    [Fact]
    public async Task RepricingWithoutPriorInputEventSuppressesSavingsBeforeSettlement()
    {
        var edits = new QuantityEditState();
        var partId = Guid.NewGuid();
        var quote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pendingReprice = edits.RepriceAsync(partId, () => quote.Task);

        Assert.True(edits.IsPending(partId));
        quote.SetException(new InvalidOperationException("Quote unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pendingReprice);
        Assert.True(edits.IsPending(partId));
    }

    [Fact]
    public async Task RepricingFailureKeepsSavingsSuppressedUntilRetrySucceeds()
    {
        var edits = new QuantityEditState();
        var partId = Guid.NewGuid();
        edits.Begin(partId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            edits.RepriceAsync(partId, () => throw new InvalidOperationException("Quote unavailable")));

        Assert.True(edits.IsPending(partId));

        await edits.RepriceAsync(partId, () => Task.CompletedTask);

        Assert.False(edits.IsPending(partId));
    }

    [Fact]
    public async Task EarlierSuccessfulQuoteCannotClearAnewerQuantityEdit()
    {
        var edits = new QuantityEditState();
        var partId = Guid.NewGuid();
        var firstQuote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        edits.Begin(partId);
        var first = edits.RepriceAsync(partId, () => firstQuote.Task);

        edits.Begin(partId);
        firstQuote.SetResult();
        await first;

        Assert.True(edits.IsPending(partId));
        await edits.RepriceAsync(partId, () => Task.CompletedTask);
        Assert.False(edits.IsPending(partId));
    }
}
