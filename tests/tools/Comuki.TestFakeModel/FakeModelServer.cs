using Comuki.TestFakeModel.Anthropic;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.Scripting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Comuki.TestFakeModel;

/// <summary>
/// In-process fake for the Anthropic Messages API (<c>POST /v1/messages</c>,
/// non-streaming and <c>stream: true</c> SSE). Reusable two ways: directly
/// from an xUnit <c>IAsyncLifetime</c> fixture — construct, <see cref="StartAsync"/>
/// in <c>InitializeAsync</c>, <see cref="DisposeAsync"/> in <c>DisposeAsync</c>,
/// same lifecycle shape as <c>Comuki.Host.Testing.MinioImage</c>'s
/// per-suite fixtures, just backed by an in-process Kestrel host instead
/// of a container — and as a standalone exe (<c>Program.cs</c>) for manual
/// smoke-testing a real <c>pi</c> binary, or packaged into a container
/// image later (WS5).
/// </summary>
public sealed class FakeModelServer : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly FakeModelState state;
    private bool started;

    /// <summary>Builds (but does not start) the server for <paramref name="options"/>.</summary>
    public FakeModelServer(FakeModelServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        state = new FakeModelState(options.Script, new FixedStepClock(options.ClockEpoch, options.ClockStep));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(FakeModelServer).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port ?? FreeTcpPort.Next()}");
        builder.Services.AddSingleton(state);

        application = builder.Build();
        application.MapAnthropicMessages();
    }

    /// <summary>The loopback base address Kestrel bound to — populated after <see cref="StartAsync"/>.</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>Every <c>POST /v1/messages</c> request observed so far, in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => state.Requests;

    /// <summary>The scenario name deterministic ids are derived from.</summary>
    public string ScenarioName => state.ScenarioName;

    /// <summary>Starts Kestrel and resolves <see cref="BaseAddress"/>.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await application.StartAsync(cancellationToken);
        BaseAddress = new Uri(
            application.Services
                .GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single());
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

    /// <summary>Clears the recorded request log and rewinds the script cursor — call between tests that reuse one instance.</summary>
    public void Reset()
    {
        state.Reset();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (started)
        {
            await StopAsync();
        }

        await application.DisposeAsync();
    }
}
