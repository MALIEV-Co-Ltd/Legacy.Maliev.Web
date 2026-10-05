using System.Diagnostics;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

// Test-only observations of actual service calls. No payload, identity, ticket or price is emitted.
internal sealed class MaterialCompletionObservation
{
    private Operation current = new();

    public void BeginEdit() => Volatile.Write(ref current, new Operation());
    public string Describe() => Volatile.Read(ref current).Describe();

    public void Decorate(IServiceCollection services)
    {
        Decorate<IInstantQuotationAuthoritativePricingService>(services, inner => new Pricing(inner, this));
        Decorate<IInstantQuotationQuoteTicketService>(services, inner => new Tickets(inner, this));
        Decorate<IInstantQuotationSessionStore>(services, inner => new Store(inner, this));
    }

    private static void Decorate<T>(IServiceCollection services, Func<T, T> wrap) where T : class
    {
        var original = services.Single(item => item.ServiceType == typeof(T));
        services.RemoveAll<T>();
        services.Add(new ServiceDescriptor(typeof(T), provider => wrap((T)(original.ImplementationInstance
            ?? original.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!))), original.Lifetime));
    }

    private sealed class Operation
    {
        private readonly object gate = new();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private DateTimeOffset? revision;
        private int entered, running, returnedNull, returnedQuote, canceled, faulted;
        private int pending, completed, unavailable, ticketEntered, ticketIssued, authorizedWrite, authorizedStored;

        public void Enter(DateTimeOffset value)
        {
            lock (gate) { revision = value; entered++; running++; }
        }

        public void Frame(InstantQuotationMaterialPricingStatus status)
        {
            lock (gate)
            {
                if (status == InstantQuotationMaterialPricingStatus.Pending) pending++;
                else if (status == InstantQuotationMaterialPricingStatus.Completed) completed++;
                else if (status == InstantQuotationMaterialPricingStatus.Unavailable) unavailable++;
            }
        }

        public void Finish(string outcome)
        {
            lock (gate)
            {
                running--;
                if (outcome == "null") returnedNull++;
                else if (outcome == "quote") returnedQuote++;
                else if (outcome == "canceled") canceled++;
                else faulted++;
            }
        }

        public void Ticket(DateTimeOffset value, bool issued)
        {
            lock (gate)
            {
                if (revision != value) return;
                if (issued) ticketIssued++;
                else ticketEntered++;
            }
        }

        public void Write(DateTimeOffset value, bool stored)
        {
            lock (gate)
            {
                if (revision != value) return;
                if (stored) authorizedStored++;
                else authorizedWrite++;
            }
        }

        public string Describe()
        {
            lock (gate)
            {
                var seconds = elapsed.Elapsed.TotalSeconds;
                var bucket = seconds < 5 ? "under5" : seconds < 15 ? "5to15" : seconds < 30 ? "15to30" : "30plus";
                return $"pricingEntered={Math.Min(entered, 9)}; pricingRunning={Math.Min(running, 9)}; returnedNull={Math.Min(returnedNull, 9)}; returnedQuote={Math.Min(returnedQuote, 9)}; canceled={Math.Min(canceled, 9)}; faulted={Math.Min(faulted, 9)}; selectedPending={Math.Min(pending, 9)}; selectedCompleted={Math.Min(completed, 9)}; selectedUnavailable={Math.Min(unavailable, 9)}; ticketEntered={Math.Min(ticketEntered, 9)}; ticketIssued={Math.Min(ticketIssued, 9)}; authorizedWriteAttempted={Math.Min(authorizedWrite, 9)}; authorizedWriteSucceeded={Math.Min(authorizedStored, 9)}; elapsedBucket={bucket}";
            }
        }
    }

    private sealed class Pricing(IInstantQuotationAuthoritativePricingService inner, MaterialCompletionObservation observation)
        : IInstantQuotationAuthoritativePricingService
    {
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? ownerIdentity,
            bool includeComparisons, CancellationToken cancellationToken) =>
            Observe(session, () => inner.QuoteAsync(session, ownerIdentity, includeComparisons, cancellationToken));

        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? ownerIdentity,
            bool includeComparisons, Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken cancellationToken)
        {
            var operation = Volatile.Read(ref observation.current);
            var selected = session.Parts.ToDictionary(part => part.PartId, part => part.Configuration.MaterialKey);
            return Observe(session, () => inner.QuoteAsync(session, ownerIdentity, includeComparisons, (frame, token) =>
            {
                if (selected.TryGetValue(frame.PartId, out var key) && string.Equals(key, frame.MaterialKey, StringComparison.Ordinal))
                    operation.Frame(frame.Status);
                return observer(frame, token);
            }, cancellationToken), operation);
        }

        private async Task<InstantQuotationOrderQuote?> Observe(InstantQuotationSessionState session,
            Func<Task<InstantQuotationOrderQuote?>> invoke, Operation? operation = null)
        {
            operation ??= Volatile.Read(ref observation.current);
            operation.Enter(session.UpdatedAt);
            try
            {
                var quote = await invoke();
                operation.Finish(quote is null ? "null" : "quote");
                return quote;
            }
            catch (OperationCanceledException) { operation.Finish("canceled"); throw; }
            catch { operation.Finish("faulted"); throw; }
        }
    }

    private sealed class Tickets(IInstantQuotationQuoteTicketService inner, MaterialCompletionObservation observation)
        : IInstantQuotationQuoteTicketService
    {
        public InstantQuotationQuoteAuthorization Issue(InstantQuotationSessionState session, InstantQuotationOrderQuote quote, DateTimeOffset now)
        {
            var operation = Volatile.Read(ref observation.current);
            operation.Ticket(session.UpdatedAt, false);
            var ticket = inner.Issue(session, quote, now);
            operation.Ticket(session.UpdatedAt, true);
            return ticket;
        }

        public bool Validate(InstantQuotationSessionState session, InstantQuotationOrderQuote quote,
            InstantQuotationQuoteAuthorization authorization, DateTimeOffset now) => inner.Validate(session, quote, authorization, now);
    }

    private sealed class Store(IInstantQuotationSessionStore inner, MaterialCompletionObservation observation)
        : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState,
            CancellationToken cancellationToken) => inner.CreateAsync(ownerIdentity, requestState, cancellationToken);

        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity,
            CancellationToken cancellationToken) => inner.GetAsync(sessionId, ownerIdentity, cancellationToken);

        public async Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken)
        {
            var operation = Volatile.Read(ref observation.current);
            if (session.QuoteAuthorization is not null) operation.Write(session.UpdatedAt, false);
            var stored = await inner.PutAsync(session, ownerIdentity, cancellationToken);
            if (stored && session.QuoteAuthorization is not null) operation.Write(session.UpdatedAt, true);
            return stored;
        }

        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            inner.RemoveAsync(sessionId, ownerIdentity, cancellationToken);
    }
}
