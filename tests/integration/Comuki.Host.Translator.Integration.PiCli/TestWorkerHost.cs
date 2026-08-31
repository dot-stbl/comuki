using Comuki.Host.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Comuki.Host.Translator.Integration.PiCli;

/// <summary>
/// Boots the real worker runtime (REST + code-first gRPC) on an in-process
/// Kestrel with two loopback listeners: HTTP/1.1 for the REST surface and
/// cleartext HTTP/2-only for gRPC. Tests connect real gRPC channels / Refit
/// clients to the returned addresses.
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

    /// <summary>The loopback REST base address (HTTP/1.1 listener).</summary>
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
    /// <param name="configureServices"></param>
    /// <param name="mapRest">Map the worker REST surface; requires orchestration application services registered.</param>
    public static async Task<TestWorkerHost> StartAsync(Action<IServiceCollection> ConfigureServices, bool mapRest = true)
    {
        var builder = WebApplication.CreateBuilder();
        // Production topology on one loopback: REST on HTTP/1.1, the worker
        // bidi stream on cleartext HTTP/2. Separate listeners because Kestrel
        // answers the h2 preface with HTTP_1_1_REQUIRED on an HTTP/1-only
        // endpoint, and mixed Http1AndHttp2 still tripped the gRPC client.
        var restPort = FreeTcpPort();
        var grpcPort = FreeTcpPort();
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            kestrelOptions.Listen(System.Net.IPAddress.Loopback, restPort, static listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
            kestrelOptions.Listen(System.Net.IPAddress.Loopback, grpcPort, static listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
        });
        ConfigureServices(builder.Services);

        var app = builder.Build();
        app.MapWorkerGrpc();
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
