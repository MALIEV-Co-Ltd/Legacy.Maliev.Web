using System.Net;
using System.Net.Sockets;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationBrowserPortTests
{
    [Fact]
    public void BrowserHostPort_IsOutsideClientPoolAndAvailableForHost()
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            int port = BrowserTestServerPort.Reserve();
            Assert.InRange(port, 20000, 29999);
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    [Fact]
    public void OccupiedCandidate_RetriesAndReleasesTheSuccessfulReservation()
    {
        int occupiedPort = BrowserTestServerPort.Reserve();
        var occupied = new TcpListener(IPAddress.Loopback, occupiedPort);
        occupied.Server.ExclusiveAddressUse = true;
        try
        {
            occupied.Start();
            int availablePort = BrowserTestServerPort.Reserve();
            var candidates = new Queue<int>([occupiedPort, availablePort]);
            Assert.Equal(availablePort, BrowserTestServerPort.Reserve(() => candidates.Dequeue()));
            Assert.Empty(candidates);
            var host = new TcpListener(IPAddress.Loopback, availablePort);
            try { host.Start(); }
            finally { host.Stop(); }
        }
        finally
        {
            occupied.Stop();
        }
    }

    [Fact]
    public void OccupiedCandidates_ExhaustExactlySixtyFourAttemptsWithoutClosingTheirOwner()
    {
        int occupiedPort = BrowserTestServerPort.Reserve();
        var occupied = new TcpListener(IPAddress.Loopback, occupiedPort);
        occupied.Server.ExclusiveAddressUse = true;
        try
        {
            occupied.Start();
            int attempts = 0;
            var failure = Assert.Throws<InvalidOperationException>(() =>
                BrowserTestServerPort.Reserve(() => { attempts++; return occupiedPort; }));
            Assert.Equal(64, attempts);
            Assert.Equal("No loopback browser-test port is available in the test-server range.", failure.Message);
            Assert.True(occupied.Server.IsBound);
        }
        finally
        {
            occupied.Stop();
        }
    }
}
