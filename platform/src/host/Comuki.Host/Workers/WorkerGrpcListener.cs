namespace Comuki.Host.Workers;

/// <summary>
/// Config key and port-pool default for the worker gRPC bidi stream's
/// DEDICATED Kestrel listener (issue #152). Bound and consumed by
/// Comuki.Host/Program.cs, which binds this port as HTTP/2-only, separate
/// from the REST/SPA listener — Kestrel does not support HTTP/2 on a
/// shared, cleartext (no-TLS) Http1AndHttp2 endpoint (it silently serves
/// HTTP/1.1 only there without TLS's ALPN), so a gRPC bidi stream sharing
/// the REST listener never actually negotiates. Confirmed by direct
/// in-process reproduction: with a single shared listener, zero
/// worker.reported journal entries land, ever — not a race.
/// </summary>
public static class WorkerGrpcListener
{
    /// <summary>Config key (env: COMUKI_HOST_WORKERGRPCPORT, TOML: [host] workerGrpcPort).</summary>
    public const string ConfigKey = "Host:WorkerGrpcPort";

    /// <summary>Port pool default (ports.md — 17185).</summary>
    public const int DefaultPort = 17185;
}
