using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>Owns only the physical web root explicitly created for the Testing application lifetime.</summary>
internal sealed class TestingWebRootLease : IDisposable
{
    private readonly object disposalGate = new();
    private int disposalState;

    private TestingWebRootLease(PhysicalFileProvider provider) => Provider = provider;

    internal PhysicalFileProvider Provider { get; }
    internal bool IsDisposed => Volatile.Read(ref disposalState) == 2;

    internal static TestingWebRootLease? Create(IWebHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Testing") || string.IsNullOrEmpty(environment.WebRootPath)) return null;

        var lease = new TestingWebRootLease(new PhysicalFileProvider(environment.WebRootPath));
        environment.WebRootFileProvider = lease.Provider;
        return lease;
    }

    public void Dispose()
    {
        lock (disposalGate)
        {
            // The lock serializes other callers; state 1 permits same-thread reentry without another attempt.
            if (disposalState != 0) return;
            disposalState = 1;
            try { Provider.Dispose(); }
            finally { Volatile.Write(ref disposalState, 2); }
        }
    }
}
