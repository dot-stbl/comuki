using System.Net;
using System.Net.Sockets;

namespace Comuki.Host.Testing;

/// <summary>Binds an ephemeral loopback TCP port for a test host, then releases it immediately.</summary>
public static class FreeTcpPort
{
    /// <summary>
    /// Returns a port number free at the moment of the call. There is an
    /// inherent release-then-rebind race — acceptable here because Kestrel
    /// binds it microseconds later, on the same machine, with nothing else
    /// competing for test ports.
    /// </summary>
    public static int Next()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}
