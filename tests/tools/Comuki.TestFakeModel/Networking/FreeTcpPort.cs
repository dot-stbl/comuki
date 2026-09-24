using System.Net;
using System.Net.Sockets;

namespace Comuki.TestFakeModel.Networking;

/// <summary>
/// Binds an ephemeral loopback TCP port, then releases it immediately.
/// Duplicated from <c>Comuki.Host.Testing.FreeTcpPort</c> rather than
/// referenced: this tool ships standalone (exe host + container image,
/// WS5) and must not depend on a test-only integration project — the same
/// reasoning <c>Comuki.Host.Integration.Proxy/FakeUpstreamServer.cs</c>
/// already applied to its own private copy.
/// </summary>
public static class FreeTcpPort
{
    /// <summary>Returns a port number free at the moment of the call.</summary>
    public static int Next()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
