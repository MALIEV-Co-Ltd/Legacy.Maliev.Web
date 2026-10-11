namespace Legacy.Maliev.Web.Tests;

/// <summary>Retains only Redis clients captured from an exact member fixture host before startup.</summary>
internal sealed class MemberRedisConnectionOwnership
{
    private readonly object gate = new();
    private readonly Dictionary<IDisposable, bool> connections = new(ReferenceEqualityComparer.Instance);
    private bool terminal;

    internal static Action ForHost(IDisposable connection)
    {
        var ownership = new MemberRedisConnectionOwnership();
        ownership.Capture(connection);
        return ownership.Release;
    }

    internal void Capture(IDisposable connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (gate)
        {
            if (terminal) throw new ObjectDisposedException(nameof(MemberRedisConnectionOwnership));
            connections.TryAdd(connection, false);
        }
    }

    internal void Release()
    {
        lock (gate)
        {
            terminal = true;
            var failures = new List<Exception>();
            foreach (var connection in connections.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray())
            {
                try
                {
                    connection.Dispose();
                    connections.Remove(connection);
                }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0)
                throw new AggregateException("Member exact Redis client release incomplete; retained instances require retry.", failures);
        }
    }
}
