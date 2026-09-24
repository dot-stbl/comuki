using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Host.Translator;
using Comuki.Host.Translator.Api.Registration;
using Comuki.Host.Translator.Execution.Loop;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Profiles;
using Comuki.Host.Translator.Runtime;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Comuki.TestFakeModel;
using Comuki.TestFakeModel.Hosting;
using Comuki.TestFakeModel.Scripting.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// T2b's <see cref="IAgentLoopHarness"/> (design.md D3 / WS7 task 7.1):
/// seeds through <see cref="RealPiFakeModelHost"/>'s real webhook exactly
/// like T2a's <see cref="AgentLoopHarness"/> does, but "starts the worker"
/// by running the real <c>TranslatorLoop</c> in-process — spawning the
/// real, vendored <c>pi</c> binary (<see cref="RealPiInstallation"/>)
/// pointed at a scenario-scripted <see cref="FakeModelServer"/> instead of
/// provisioning a container. No Docker/Podman dependency anywhere in this
/// class — see the class remarks on <see cref="RealPiFakeModelHost"/> for
/// why that trade-off was made deliberately.
/// </summary>
/// <remarks>
/// Issue #150 (this is the test that proves/needs it): pi's built-in model
/// catalog hardcodes a per-model <c>baseUrl</c> for every cataloged
/// Anthropic model (verified against the vendored 0.85.1 bundle — grepping
/// its shipped JS for <c>ANTHROPIC_BASE_URL</c> finds zero references at
/// all); only a <c>providers.anthropic.baseUrl</c> entry in
/// <c>models.json</c> under <c>PI_CODING_AGENT_DIR</c> redirects it
/// (verified with a real pi process against a throwaway stub server).
/// Comuki's production Translator (<c>PiEnvironment.FromClaim</c>) stamps
/// only <c>ANTHROPIC_BASE_URL</c>/<c>ANTHROPIC_AUTH_TOKEN</c> today — no
/// <c>models.json</c> is written anywhere in <c>platform/src</c>. This
/// harness therefore does NOT rely on that stamp to redirect pi (it would
/// silently no-op, exactly demonstrating the bug): it independently sets
/// <see cref="PiCodingAgentDirVariable"/>/<see cref="AnthropicAuthTokenVariable"/>
/// on this TEST process's own environment before running the translator
/// loop, standing in for the still-missing production fix — see the
/// WS7 report for the exact production change this proves is needed.
/// </remarks>
public sealed class RealPiFakeModelHarness(RealPiInstallation realPi, RealPiFakeModelHost host) : IAgentLoopHarness, IAsyncDisposable
{
    /// <summary>The claim label every scenario this harness serves declares as <c>worker.image</c> — never actually built/pulled, T2b runs no container.</summary>
    public const string WorkerImageLabel = "comuki-agent-test-worker:ws7-real-pi";

    private const int IssueNumber = 9101;
    private const string PiCodingAgentDirVariable = "PI_CODING_AGENT_DIR";
    private const string AnthropicAuthTokenVariable = "ANTHROPIC_AUTH_TOKEN";
    private const string FakeAuthTokenValue = "real-pi-fake-model-test-token";

    private readonly List<string> scratchDirectories = [];

    private FakeModelServer? fakeModelServer;
    private ServiceProvider? translatorProvider;
    private Guid seededWorkItemId;
    private string workingDirectory = string.Empty;

    /// <summary>The fake model's own observed-request log for the one scenario this harness ran — asserted against directly by the test (WS7's "prove requests reach the fake model").</summary>
    public IReadOnlyList<RecordedRequest> FakeModelRequests => fakeModelServer?.Requests ?? [];

    /// <inheritdoc />
    public async Task<SeededWorkItem> SeedTicketAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default)
    {
        var seeded = await host.SeedTicketAsync(scenario.Ticket.Title, scenario.Ticket.Body, scenario.Ticket.Labels, IssueNumber, cancellationToken);
        seededWorkItemId = seeded.WorkItemId;
        return seeded;
    }

    /// <inheritdoc />
    public async Task<WorkerHandle> StartWorkerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default)
    {
        if (scenario.Model is not { FakeScript: { Length: > 0 } fakeScriptRelativePath })
        {
            throw new InvalidOperationException(
                $"scenario '{scenario.Name}' declares no model.fakeScript — RealPiFakeModelHarness has nothing to script the fake model with.");
        }

        var fakeScriptPath = ScenarioLoader.ResolveRelativeToScenario(scenario.SourcePath, fakeScriptRelativePath);
        var script = FakeScriptLoader.LoadFromFile(fakeScriptPath, scenario.Name);
        fakeModelServer = new FakeModelServer(new FakeModelServerOptions { Script = script });
        await fakeModelServer.StartAsync(cancellationToken);

        workingDirectory = CopyFixtureRepo(scenario.Ticket.TargetRepo.Fixture);
        var agentDirectory = CreateScratchDirectory("agent-dir");
        await WriteModelsJsonAsync(agentDirectory, fakeModelServer.BaseAddress, cancellationToken);

        var options = new TranslatorOptions
        {
            OrchestratorBaseUrl = host.BaseAddress,
            OrchestratorGrpcUrl = host.GrpcAddress,
            WorkerToken = host.ResolveTokenIssuer().Issue(WorkerId.New()),
            ProfileKey = scenario.Worker.ProfileKey,
            ProfilesRef = scenario.Worker.ProfilesRef,
            WorkerImage = scenario.Worker.Image,
            PiExecutable = realPi.ExecutablePath,
            WorkingDirectory = workingDirectory,
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            ClaimPollInterval = TimeSpan.FromSeconds(1),
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPiRunner, PiRunner>();
        services.AddSingleton<IProfilesProvider, ProfilesProvider>();
        services.AddSingleton<HeartbeatMonitor>();
        services.AddSingleton<TranslatorLoop>();
        services.AddOrchestratorApi();
        services.AddWorkerGrpcClient();
        translatorProvider = services.BuildServiceProvider();

        var loop = translatorProvider.GetRequiredService<TranslatorLoop>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        bool claimed;
        using (StampAmbientPiEnvironment(agentDirectory))
        {
            claimed = await loop.TryRunOnceAsync(linked.Token);
        }

        return claimed
            ? new WorkerHandle(WorkerId.New(), "in-process-real-pi")
            : throw new InvalidOperationException(
                "the real-pi translator loop found nothing to claim — the webhook-seeded work item never reached "
                    + "Queued, or its claim labels don't match RealPiFakeModelHost's Intake:Worker:* config.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return host.ReadTimelineAsync(runId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> ReadWorkItemStatusAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return host.ReadWorkItemStatusAsync(workItemId, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopWorkerAsync(WorkerHandle handle, CancellationToken cancellationToken = default)
    {
        // Nothing to stop — StartWorkerAsync already ran the one translator
        // cycle to completion (T2b is synchronous/in-process, no background
        // container to tear down). Cleanup happens in DisposeAsync.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> ResolveWorkingDirectoryAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(workItemId == seededWorkItemId && workingDirectory.Length > 0 ? workingDirectory : null);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (translatorProvider is not null)
        {
            await translatorProvider.DisposeAsync();
        }

        if (fakeModelServer is not null)
        {
            await fakeModelServer.DisposeAsync();
        }

        foreach (var directory in scratchDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup — see RealPiInstallation.DisposeAsync's identical tolerance.
            }
            catch (UnauthorizedAccessException)
            {
                // Same tolerance.
            }
        }
    }

    /// <summary>
    /// Sets <see cref="PiCodingAgentDirVariable"/>/<see cref="AnthropicAuthTokenVariable"/>
    /// on this process's own environment for the duration of one translator
    /// cycle, restoring whatever was there before on dispose — narrows the
    /// window a process-global env mutation is visible to any other
    /// collection running concurrently in this same test assembly. This
    /// stands in for the still-missing production Translator change (see
    /// this class's own remarks / the WS7 report) — <c>PiEnvironment.FromClaim</c>
    /// never sets <see cref="PiCodingAgentDirVariable"/> today.
    /// </summary>
    private static IDisposable StampAmbientPiEnvironment(string agentDirectory)
    {
        var previousAgentDir = Environment.GetEnvironmentVariable(PiCodingAgentDirVariable);
        var previousAuthToken = Environment.GetEnvironmentVariable(AnthropicAuthTokenVariable);
        Environment.SetEnvironmentVariable(PiCodingAgentDirVariable, agentDirectory, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(AnthropicAuthTokenVariable, FakeAuthTokenValue, EnvironmentVariableTarget.Process);

        return new RestoreEnvironmentOnDispose(previousAgentDir, previousAuthToken);
    }

    private static async Task WriteModelsJsonAsync(string agentDirectory, Uri fakeModelBaseAddress, CancellationToken cancellationToken)
    {
        // "Overriding Built-in Providers" (pi docs/models.md): the anthropic
        // provider's baseUrl override merges into the built-in catalog —
        // every cataloged Claude model id stays selectable, just redirected.
        var modelsJson =
            /*lang=json,strict*/ $$"""
            {
              "providers": {
                "anthropic": {
                  "baseUrl": "{{fakeModelBaseAddress}}"
                }
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(agentDirectory, "models.json"), modelsJson, cancellationToken);
    }

    private string CreateScratchDirectory(string suffix)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"comuki-real-pi-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        scratchDirectories.Add(directory);
        return directory;
    }

    private string CopyFixtureRepo(string fixtureName)
    {
        var source = Path.Combine(RepositoryRoot(), "tests", "fixtures", "target-repos", fixtureName);
        if (!Directory.Exists(source))
        {
            throw new InvalidOperationException($"target-repo fixture '{fixtureName}' not found at '{source}'");
        }

        var target = CreateScratchDirectory("workdir");
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, directory[source.Length..].TrimStart(Path.DirectorySeparatorChar)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, file[source.Length..].TrimStart(Path.DirectorySeparatorChar)), overwrite: true);
        }

        return target;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "comuki.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("could not locate the repo root (comuki.slnx) from " + AppContext.BaseDirectory);
    }

    /// <summary>Restores the two ambient pi env vars <see cref="StampAmbientPiEnvironment"/> overwrote.</summary>
    private sealed class RestoreEnvironmentOnDispose(string? previousAgentDir, string? previousAuthToken) : IDisposable
    {
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(PiCodingAgentDirVariable, previousAgentDir, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(AnthropicAuthTokenVariable, previousAuthToken, EnvironmentVariableTarget.Process);
        }
    }
}
