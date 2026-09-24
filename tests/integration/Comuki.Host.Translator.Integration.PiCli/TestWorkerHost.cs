using Comuki.Host.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Comuki.Host.Translator.Integration.PiCli;

/// <summary>
/// Boots the real worker runtime (REST + code-first gRPC) on an in-process
/// Kestrel with two loopback listeners: HTTP/1.1 (+ HTTP/2 once TLS is on)
/// for the REST surface and cleartext HTTP/2-only for gRPC. Tests connect
/// real gRPC channels / Refit clients to the returned addresses. Matches
/// Comuki.Host/Program.cs's production topology (issue #152) — a dedicated
/// HTTP/2-only listener for the worker gRPC stream, since Kestrel does
/// not negotiate HTTP/2 over a shared, cleartext Http1AndHttp2 listener
/// without TLS's ALPN.
/// </summary>
public sealed class TestWorkerHost : IAsyncDisposable
{
    private readonly WebApplication app;

    private TestWorkerHost(WebApplication app, Uri restAddress, Uri grpcAddress)
    {
        this.app = app;
        BaseAddress = restAddress;
        GrpcAddress = grpcAddress;
    }

    /// <summary>The loopback REST base address (Http1AndHttp2 listener).</summary>
    public Uri BaseAddress { get; }

    /// <summary>The loopback gRPC address (cleartext HTTP/2 listener).</summary>
    public Uri GrpcAddress { get; }

    /// <summary>Resolves services from the host's root provider.</summary>
    /// <typeparam name="T"></typeparam>
    public T GetService<T>()
        where T : notnull
    {
        return app.Services.GetRequiredService<T>();
    }

    /// <summary>Creates a scope from the host's root provider.</summary>
    public IServiceScope CreateScope()
    {
        return app.Services.CreateScope();
    }

    /// <summary>Builds and starts the host with the caller's extra services.</summary>
    /// <param name="ConfigureServices">Extra services registered into the host DI container before it starts.</param>
    /// <param name="mapRest">Map the worker REST surface; requires orchestration application services registered.</param>
    public static async Task<TestWorkerHost> StartAsync(Action<IServiceCollection> ConfigureServices, bool mapRest = true)
    {
        var builder = WebApplication.CreateBuilder();
        // Production topology on one loopback: REST on HTTP/1+HTTP/2
        // (Http1AndHttp2 — matches Comuki.Host/Program.cs post-fix), the
        // worker bidi stream on its own cleartext HTTP/2 listener. The
        // pre-fix production shape (mixed Http1AndHttp2, single shared
        // listener) is reproduced by StartWithSharedListenerAsync below —
        // a regression guard, not a topology any test should depend on.
        var restPort = FreeTcpPort();
        var grpcPort = FreeTcpPort();
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            kestrelOptions.Listen(System.Net.IPAddress.Loopback, restPort, static listenOptions => listenOptions.Protocols = HttpProtocols.Http1AndHttp2);
            kestrelOptions.Listen(System.Net.IPAddress.Loopback, grpcPort, static listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
        });
        ConfigureServices(builder.Services);

        var app = builder.Build();
        app.MapWorkerGrpc(grpcPort);
        if (mapRest)
        {
            app.MapWorkerRest();
        }

        await app.StartAsync(TestContext.Current.CancellationToken);

        return new TestWorkerHost(
            app,
            new Uri($"http://127.0.0.1:{restPort}/"),
            new Uri($"http://127.0.0.1:{grpcPort}/"));
    }

    /// <summary>
    /// Characterization fixture for issue #152: ONE shared, cleartext
    /// Http1AndHttp2 listener for both REST and gRPC — production's
    /// listener config BEFORE this fix (Comuki.Host/Program.cs no longer
    /// does this). Kestrel does not support HTTP/2 negotiation on a mixed
    /// Http1AndHttp2 endpoint without TLS — it silently falls back to
    /// HTTP/1.1 only (logs a one-time warning), so the worker gRPC bidi
    /// stream never actually negotiates here and every event it would
    /// carry is lost. Exists to prove the bug's exact mechanism and guard
    /// against ever silently reintroducing a shared listener.
    /// </summary>
    public static async Task<TestWorkerHost> StartWithSharedListenerAsync(Action<IServiceCollection> ConfigureServices, bool mapRest = true)
    {
        var builder = WebApplication.CreateBuilder();
        var port = FreeTcpPort();
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            kestrelOptions.Listen(System.Net.IPAddress.Loopback, port, static listenOptions => listenOptions.Protocols = HttpProtocols.Http1AndHttp2);
        });
        ConfigureServices(builder.Services);

        var app = builder.Build();
        app.MapWorkerGrpc();
        if (mapRest)
        {
            app.MapWorkerRest();
        }

        await app.StartAsync(TestContext.Current.CancellationToken);

        var address = new Uri($"http://127.0.0.1:{port}/");
        return new TestWorkerHost(app, address, address);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
    }

    private static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
