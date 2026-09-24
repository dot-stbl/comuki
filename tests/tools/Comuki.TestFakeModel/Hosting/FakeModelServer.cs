using Comuki.TestFakeModel.Anthropic;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.Networking;
using Comuki.TestFakeModel.OpenAi;
using Comuki.TestFakeModel.Scripting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Comuki.TestFakeModel.Hosting;

/// <summary>
/// In-process <c>fake</c>-mode server for both the Anthropic Messages API
/// (<c>POST /v1/messages</c>) and the OpenAI Chat Completions API
/// (<c>POST /v1/chat/completions</c>) — non-streaming and <c>stream: true</c>
/// SSE for either. Both endpoints share one <see cref="FakeModelState"/>
/// instance, so a single fakeScript backs whichever wire shape the caller
/// speaks — design.md's "no code path knows it isn't talking to a real
/// provider". <c>replay</c>/<c>record</c> mode live in the sibling
/// <c>Cassettes.Hosting.CassetteModelServer</c> (WS5): a cassette-backed
/// server doesn't need a fakeScript at all, so keeping the two server
/// types separate avoids threading an unused mode branch through this
/// one's otherwise-simple constructor. Reusable two ways: directly from an
/// xUnit <c>IAsyncLifetime</c> fixture — construct, <see cref="StartAsync"/>
/// in <c>InitializeAsync</c>, <see cref="DisposeAsync"/> in <c>DisposeAsync</c>,
/// same lifecycle shape as <c>Comuki.Host.Testing.MinioImage</c>'s
/// per-suite fixtures, just backed by an in-process Kestrel host instead
/// of a container — and as a standalone exe (<c>Program.cs</c>) for manual
/// smoke-testing a real <c>pi</c> binary, or packaged into the container
/// image (WS5, <c>Dockerfile</c>).
/// </summary>
public sealed class FakeModelServer : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly FakeModelState state;
    private bool started;

    /// <summary>Builds (but does not start) the server for <paramref name="options"/>.</summary>
    public FakeModelServer(FakeModelServerOptions options)
    {
        state = new FakeModelState(options.Script, new FixedStepClock(options.ClockEpoch, options.ClockStep));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(FakeModelServer).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port ?? FreeTcpPort.Next()}");
        builder.Services.AddSingleton(state);

        application = builder.Build();
        application.MapAnthropicMessages();
        application.MapOpenAiChatCompletions();
    }

    /// <summary>The loopback base address Kestrel bound to — populated after <see cref="StartAsync"/>.</summary>
    // boundary: set inside StartAsync before any caller can observe it; there is no
    // meaningful default before the server has started.
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>Every request observed so far (either protocol), in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => state.Requests;

    /// <summary>The scenario name deterministic ids are derived from.</summary>
    public string ScenarioName => state.ScenarioName;

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
