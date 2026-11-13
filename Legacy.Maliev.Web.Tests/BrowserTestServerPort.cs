using System.Net;
using System.Net.Sockets;

namespace Legacy.Maliev.Web.Tests;

internal static class BrowserTestServerPort
{
    internal static int Reserve() => Reserve(() => Random.Shared.Next(20000, 30000));

    internal static int Reserve(Func<int> nextPort)
    {
        ArgumentNullException.ThrowIfNull(nextPort);
        // Keep quotation browser listeners outside the default Windows outbound client-port pool.
        for (int attempt = 0; attempt < 64; attempt++)
        {
            int port = nextPort();
            if (port is < 20000 or >= 30000) throw new ArgumentOutOfRangeException(nameof(nextPort));
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                // Another task or an excluded port range owns this candidate.
            }
            finally
            {
                listener.Stop();
            }
        }

        throw new InvalidOperationException("No loopback browser-test port is available in the test-server range.");
    }
}
