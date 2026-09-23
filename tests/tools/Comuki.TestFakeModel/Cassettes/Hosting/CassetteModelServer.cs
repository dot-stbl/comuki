using Comuki.TestFakeModel.Cassettes.Endpoints;
using Comuki.TestFakeModel.Cassettes.IO;
using Comuki.TestFakeModel.Cassettes.Recording;
using Comuki.TestFakeModel.Cassettes.Replay;
using Comuki.TestFakeModel.Networking;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Comuki.TestFakeModel.Cassettes.Hosting;

/// <summary>
/// The <c>replay</c>/<c>record</c> mode server — sibling to
/// <c>Hosting.FakeModelServer</c> (which owns <c>fake</c> mode). Serves
/// (or forwards-and-captures) any POST path via one catch-all route
/// (<see cref="CassetteReplayEndpoint"/>/<see cref="CassetteRecordingEndpoint"/>):
/// unlike fake mode, a cassette-backed exchange doesn't need per-protocol
/// response rendering — it replays or forwards the exact bytes a real
/// upstream produced. Same reusable-two-ways shape as
/// <c>FakeModelServer</c>: an xUnit <c>IAsyncLifetime</c> fixture, or
/// <c>Program.cs</c>'s standalone/container-image entry point.
/// </summary>
public sealed class CassetteModelServer : IAsyncDisposable
{
    private readonly WebApplication application;
    private bool started;

    /// <summary>Builds (but does not start) the server for <paramref name="options"/>.</summary>
    public CassetteModelServer(CassetteModelServerOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(CassetteModelServer).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port ?? FreeTcpPort.Next()}");

        if (options.Mode == CassetteModelMode.Record)
        {
            if (options.UpstreamBaseUrl is null)
            {
                throw new InvalidOperationException("CassetteModelServerOptions.UpstreamBaseUrl is required when Mode is Record.");
            }

            var upstreamBaseUrl = options.UpstreamBaseUrl;
            builder.Services.AddHttpClient(CassetteUpstreamForwarder.HttpClientName);
            builder.Services.AddSingleton(serviceProvider =>
                new CassetteUpstreamForwarder(upstreamBaseUrl, serviceProvider.GetRequiredService<IHttpClientFactory>()));
            builder.Services.AddSingleton(serviceProvider => new CassetteRecordingState(
                options.CassettePath,
                options.RecordedAgainst,
                options.Scenario,
                options.Clock,
                serviceProvider.GetRequiredService<CassetteUpstreamForwarder>()));

            application = builder.Build();
            application.MapCassetteRecording();
        }
        else if (options.Mode == CassetteModelMode.Replay)
        {
            builder.Services.AddSingleton(new CassettePlaybackState(CassetteReader.LoadFromFile(options.CassettePath)));

            application = builder.Build();
            application.MapCassetteReplay();
        }
        else
        {
            throw new InvalidOperationException($"CassetteModelServerOptions.Mode must be Replay or Record, was '{options.Mode.Value}'.");
        }
    }

    /// <summary>The loopback base address Kestrel bound to. Throws until <see cref="StartAsync"/> has completed.</summary>
    public Uri BaseAddress { get => field ?? throw new InvalidOperationException("BaseAddress is not available until StartAsync has completed."); private set; }

    /// <summary>Starts Kestrel and resolves <see cref="BaseAddress"/>.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await application.StartAsync(cancellationToken);
        // boundary: Kestrel always publishes at least one address once StartAsync
        // completes for a host configured with UseUrls — IServerAddressesFeature
        // itself is never absent on the Kestrel server implementation.
        BaseAddress = new Uri(application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        started = true;
    }

    /// <summary>Stops Kestrel. Safe to call when the server was never started.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!started)
        {
            return;
        }

        await application.StopAsync(cancellationToken);
        started = false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (started)
        {
            await StopAsync();
        }

        // ASP.NET Core's DI container disposes every IDisposable singleton it
        // created (Recording.CassetteRecordingState's SemaphoreSlim included)
        // as part of disposing the root service provider here — no extra
        // teardown step needed for record mode.
        await application.DisposeAsync();
    }
}
