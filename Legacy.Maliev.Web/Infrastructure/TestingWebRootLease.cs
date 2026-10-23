using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>Owns only the physical web root explicitly created for the Testing application lifetime.</summary>
internal sealed class TestingWebRootLease : IDisposable
{
    private int disposed;

    private TestingWebRootLease(PhysicalFileProvider provider) => Provider = provider;

    internal PhysicalFileProvider Provider { get; }
    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

    internal static TestingWebRootLease? Create(IWebHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Testing") || string.IsNullOrEmpty(environment.WebRootPath)) return null;

        var lease = new TestingWebRootLease(new PhysicalFileProvider(environment.WebRootPath));
        environment.WebRootFileProvider = lease.Provider;
        return lease;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) Provider.Dispose();
    }
}
