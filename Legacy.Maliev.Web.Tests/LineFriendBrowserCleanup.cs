using System.Runtime.ExceptionServices;

namespace Legacy.Maliev.Web.Tests;

internal sealed record LineFriendClose(string Kind, Func<Task> Close, string[]? DependsOn = null);
internal sealed record LineFriendCloseReceipt(string Kind, string InvocationState, string ActualCloseState,
    bool DependencyBlocked, bool WaitExpired, string? FailureType);
internal sealed record LineFriendCloseObservation(Guid CustodyId, LineFriendCloseReceipt[] Receipts, Task Completion, bool Retained);

/// <summary>At most eight owner graphs admitted before resources are allocated. Expiry is an ownership lease, never termination.</summary>
internal static class LineFriendBrowserCleanup
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, Lease> Retained = [];
    internal sealed class Lease(bool resourceBacked) : IDisposable
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal DateTimeOffset Expires { get; } = DateTimeOffset.UtcNow.AddMinutes(2);
        internal bool ResourceBacked { get; } = resourceBacked;
        internal Dictionary<string, Node> Nodes { get; } = [];
        internal Task Completion { get; set; } = Task.CompletedTask;
        internal bool Started { get; set; }
        public void Dispose()
        {
            lock (Gate) if (!Started) Retained.Remove(Id);
        }
    }
    internal sealed class Node(LineFriendClose owner)
    {
        internal LineFriendClose Owner { get; } = owner;
        internal Task<Task>? Invocation;
        internal Task? Actual;
        internal Task Observer = Task.CompletedTask;
        internal Exception? Failure;
        internal bool Success, Blocked;
    }
    internal static Lease Reserve(bool resourceBacked = true)
    {
        lock (Gate)
        {
            if (Retained.Count >= 8) throw new InvalidOperationException("Browser cleanup custody capacity exhausted before allocation.");
            var lease = new Lease(resourceBacked); Retained.Add(lease.Id, lease); return lease;
        }
    }
    internal static bool HasCustody(Guid id) { lock (Gate) return Retained.ContainsKey(id); }
    internal static void ReleaseSyntheticControl(Lease lease)
    {
        if (lease.ResourceBacked || !lease.Completion.IsCompleted) throw new InvalidOperationException("Only terminal synthetic controls can be released.");
        lock (Gate) Retained.Remove(lease.Id);
    }
    internal static async Task<LineFriendCloseObservation> CloseAsync(Lease lease, LineFriendClose[] owners,
        Exception? primary, TimeSpan budget, Action<LineFriendCloseReceipt[]> record)
    {
        if (owners.Length > 6 || budget < TimeSpan.Zero || budget > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(owners));
        var allowed = new HashSet<string>(["context", "browser", "playwright", "client", "factory", "root"]);
        var seen = new HashSet<string>();
        foreach (var owner in owners)
        {
            if (!allowed.Contains(owner.Kind) || !seen.Add(owner.Kind)
                || (owner.DependsOn ?? []).Any(dependency => !seen.Contains(dependency) || dependency == owner.Kind))
                throw new ArgumentException("Owner graph must be unique and ordered.");
        }
        lock (Gate)
        {
            if (lease.Started || !Retained.ContainsKey(lease.Id)) throw new InvalidOperationException("Custody lease is not available.");
            lease.Started = true;
            foreach (var owner in owners) lease.Nodes.Add(owner.Kind, new(owner));
            foreach (var node in lease.Nodes.Values) node.Observer = ObserveAsync(lease, node);
            lease.Completion = CompleteAsync(lease);
        }
        var expired = false;
        try { await lease.Completion.WaitAsync(budget); }
        catch (TimeoutException) { expired = true; }
        LineFriendCloseReceipt[] rows;
        Exception? failure;
        lock (Gate)
        {
            rows = lease.Nodes.Values.Select(node => new LineFriendCloseReceipt(node.Owner.Kind,
                node.Invocation?.Status.ToString() ?? "NotStarted", node.Actual?.Status.ToString() ?? "NotReturned",
                node.Blocked, expired, node.Failure?.GetType().Name)).ToArray();
            failure = lease.Nodes.Values.Select(node => node.Failure).FirstOrDefault(value => value is not null);
            if (!lease.Completion.IsCompleted || lease.Nodes.Values.Any(node => !node.Success))
                failure ??= new TimeoutException("Owned browser cleanup remains in custody.");
        }
        try { record(rows); }
        catch (Exception exception) { failure ??= exception; }
        if (failure is not null)
        {
            // Registration precedes throwing, and a hostile Data implementation cannot replace failure identity.
            try { failure.Data["line-friend-custody"] = lease.Id; } catch { }
            if (primary is null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return new(lease.Id, rows, lease.Completion, HasCustody(lease.Id));
    }
    private static async Task ObserveAsync(Lease lease, Node node)
    {
        foreach (var dependency in node.Owner.DependsOn ?? [])
        {
            var parent = lease.Nodes[dependency]; await parent.Observer;
            lock (Gate) if (!parent.Success) { node.Blocked = true; return; }
        }
        try
        {
            // Keep the actual invocation task separately; a synchronous Close call cannot block another observer.
            var invocation = Task.Factory.StartNew(node.Owner.Close, CancellationToken.None,
                TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            lock (Gate) node.Invocation = invocation;
            var actual = await invocation;
            lock (Gate) node.Actual = actual;
            await actual; // Durable observer also observes faults delivered after the caller's wait expires.
            lock (Gate) node.Success = true;
        }
        catch (Exception exception) { lock (Gate) node.Failure = exception; }
    }
    private static async Task CompleteAsync(Lease lease)
    {
        await Task.WhenAll(lease.Nodes.Values.Select(node => node.Observer));
        lock (Gate) if (lease.Nodes.Values.All(node => node.Success)) Retained.Remove(lease.Id);
        // Failed/blocked graphs retain delegates and actual tasks for owner recovery, including after lease expiry.
    }
}
