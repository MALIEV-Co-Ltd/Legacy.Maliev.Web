namespace Legacy.Maliev.Web.Tests;

/// <summary>Owned in-memory handler graphs; no network, backend, or process is created.</summary>
public sealed class CurrentCompanySourceObservationTests
{
    [Fact]
    public void LinkedInstancesAreObservedWithoutMutation()
    {
        using var terminal = new Terminal();
        using var inner = new Link { InnerHandler = terminal };
        using var middle = new Link { InnerHandler = inner };
        using var outer = new Link { InnerHandler = middle };
        CurrentCompanySourceObservation.VerifyLinkedOrder(outer, inner);
        Assert.Same(middle, outer.InnerHandler);
        Assert.Same(inner, middle.InnerHandler);
        Assert.Same(terminal, inner.InnerHandler);
    }

    [Fact]
    public void ReversedInstancesRefuseProof()
    {
        using var terminal = new Terminal();
        using var inner = new Link { InnerHandler = terminal };
        using var outer = new Link { InnerHandler = inner };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CurrentCompanySourceObservation.VerifyLinkedOrder(inner, outer));
    }

    [Fact]
    public void SameInstanceRefusesProof()
    {
        using var terminal = new Terminal();
        using var handler = new Link { InnerHandler = terminal };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CurrentCompanySourceObservation.VerifyLinkedOrder(handler, handler));
    }

    [Fact]
    public void MissingInstanceRefusesProof()
    {
        using var terminal = new Terminal();
        using var handler = new Link { InnerHandler = terminal };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CurrentCompanySourceObservation.VerifyLinkedOrder(handler, null));
    }

    [Fact]
    public void CycleRefusesProofBeforeUnboundedTraversal()
    {
        using var terminal = new Terminal();
        using var first = new Link();
        using var second = new Link();
        using var absent = new Link { InnerHandler = terminal };
        first.InnerHandler = second;
        second.InnerHandler = first;
        try
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CurrentCompanySourceObservation.VerifyLinkedOrder(first, absent));
        }
        finally
        {
            // Only these owned synthetic handlers are mutated to break disposal recursion.
            first.InnerHandler = terminal;
            second.InnerHandler = terminal;
        }
    }

    [Fact]
    public void TraversalLimitRefusesAnUnboundedChain()
    {
        using var terminal = new Terminal();
        var handlers = Enumerable.Range(0, 66).Select(_ => new Link()).ToArray();
        try
        {
            for (var index = 0; index < handlers.Length - 1; index++)
            {
                handlers[index].InnerHandler = handlers[index + 1];
            }
            handlers[^1].InnerHandler = terminal;
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CurrentCompanySourceObservation.VerifyLinkedOrder(handlers[0], handlers[^1]));
        }
        finally
        {
            foreach (var handler in handlers)
            {
                handler.Dispose();
            }
        }
    }

    private sealed class Link : DelegatingHandler
    {
    }

    private sealed class Terminal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("These owned control graphs never send a request.");
    }
}
