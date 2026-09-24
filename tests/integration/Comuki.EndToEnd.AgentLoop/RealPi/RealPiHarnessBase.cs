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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// The shared scaffolding both real-pi harnesses
/// (<see cref="RealPiFakeModelHarness"/> for <c>model.mode: fake</c>, and
/// <see cref="ReplayPiFakeModelHarness"/> for <c>model.mode: replay</c> —
/// and the <c>CassetteGeneratingPiFakeModelHarness</c> one-shot
/// cassette-recording generator) sit on: scratch-dir management,
/// fixture-repo copy, the <c>models.json</c> <c>providers.anthropic.baseUrl</c>
/// write (issue #150 — ANTHROPIC_BASE_URL alone doesn't redirect real pi's
/// hardcoded per-model Anthropic baseUrl), the ambient
/// <c>PI_CODING_AGENT_DIR</c>/<c>ANTHROPIC_AUTH_TOKEN</c> stamp-and-restore
/// for the duration of one translator cycle, the
/// <see cref="TranslatorLoop"/> wiring (the same composition
/// <c>comuki-worker-sdk</c>'s TranslatorE2EShould proves in
/// <c>Comuki.Host.Translator.Integration.PiCli</c>), the
/// <see cref="RealPiFakeModelHost"/> seeding/timeline/status reads, and
/// the dispose-and-cleanup.
/// </summary>
/// <remarks>
/// Before WS8, <see cref="RealPiFakeModelHarness"/> was the only real-pi
/// harness and carried all of that inline — fine for one mode, but
/// adding a second mode (<c>replay</c>) would have meant a 90% copy
/// with only "which model server to start, where its BaseAddress comes
/// from" varying. This base is the shape WS8 settled on: a single
/// template-method <see cref="StartWorkerAsync"/> whose only extension
/// point is the abstract <see cref="StartModelServerAsync"/>, returning
/// the BaseAddress (cassette replay, scripted fake, or a recording
/// proxy's own) that pi will be redirected at for this run.
/// </remarks>
public abstract class RealPiHarnessBase(RealPiInstallation realPi, RealPiFakeModelHost host) : IAgentLoopHarness, IAsyncDisposable
{
    /// <summary>
    /// Base seed-ticket number every real-pi harness offsets from. A single
    /// fixed number collided once more than one harness actually called
    /// <see cref="SeedTicketAsync"/> in the same suite process (observed:
    /// WS8's <c>RecordCassetteShould</c> running alongside
    /// <see cref="RealPiFakeModelHarness"/>'s own fact) — the webhook
    /// admission layer dedupes on ticket identity (repo + issue number), so
    /// a second webhook for the same number is rejected as <c>"duplicate"</c>,
    /// not admitted as a new ticket. <see cref="NextIssueNumber"/> draws a
    /// random per-call offset instead, so every call's issue number is
    /// effectively unique within one process run without each harness
    /// needing its own hand-picked constant (WS9's live harness reuses this
    /// same base and would otherwise hit the identical collision).
    /// </summary>
    private const int IssueNumberBase = 9100;

    /// <summary>
    /// The shared worker-image label every scenario this base serves declares
    /// as <c>worker.image</c> — and the label <see cref="RealPiFakeModelHost"/>'s
    /// <c>Intake:Worker:Image</c> config is set to. A single label across
    /// <see cref="RealPiFakeModelHarness"/>/<see cref="ReplayPiFakeModelHarness"/>
    /// (and the cassette generator) means the queue claim SQL's
    /// <c>image = @image</c> filter matches whichever scenario the in-flight
    /// translator loop is targeting. Different labels per harness would
    /// require a per-scenario host config slot this shared collection
    /// doesn't have (and isn't worth adding for two scenarios).
    /// </summary>
    public const string WorkerImageLabel = "comuki-agent-test-worker:ws7-real-pi";

    private readonly List<string> scratchDirectories = [];
    private ServiceProvider? translatorProvider;
    private Guid seededWorkItemId;
    private string workingDirectory = string.Empty;

    /// <summary>
    /// Subclass entry point: stand up whichever model server this scenario
    /// declares via <see cref="ScenarioModel.Mode"/> — a scripted
    /// <c>FakeModelServer</c>, a <c>CassetteModelServer</c> in <c>replay</c>
    /// mode, or a recording <c>CassetteModelServer</c> wrapping a scripted
    /// upstream — and return its loopback BaseAddress (the value
    /// <c>models.json</c>'s <c>providers.anthropic.baseUrl</c> redirects
    /// real pi at).
    /// </summary>
    /// <param name="scenario">The scenario being run; <see cref="ScenarioDefinition.Model"/> carries the mode/shape information.</param>
    /// <param name="cancellationToken">Forwarded to the server's <c>StartAsync</c>.</param>
    protected abstract Task<Uri> StartModelServerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<SeededWorkItem> SeedTicketAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default)
    {
        var seeded = await host.SeedTicketAsync(scenario.Ticket.Title, scenario.Ticket.Body, scenario.Ticket.Labels, NextIssueNumber(), cancellationToken);
        seededWorkItemId = seeded.WorkItemId;
        return seeded;
    }

    /// <summary>
    /// Draws a random issue number offset from <see cref="IssueNumberBase"/>
    /// — see that constant's remarks for why this isn't a fixed value. Uses
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
    /// rather than <see cref="Random"/> only because CA5394 flags the
    /// latter repo-wide as warnings-as-errors — there is no actual security
    /// property riding on this number, it only needs to not collide with
    /// another call in the same process.
    /// </summary>
    private static int NextIssueNumber()
    {
        return IssueNumberBase + System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, 90000);
    }

    /// <inheritdoc />
    public async Task<WorkerHandle> StartWorkerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default)
    {
        var modelBaseAddress = await StartModelServerAsync(scenario, cancellationToken);

        workingDirectory = CopyFixtureRepo(scenario.Ticket.TargetRepo.Fixture);
        var agentDirectory = CreateScratchDirectory("agent-dir");
        await PiModelsJsonWriter.WriteAsync(agentDirectory, modelBaseAddress, cancellationToken);

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
        using (AmbientPiEnvironmentStamper.Stamp(agentDirectory))
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
        // cycle to completion (real pi is synchronous/in-process, no background
        // container to tear down). Per-server cleanup happens in
        // DisposeAsync on the subclass.
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

        await DisposeModelServerAsync();

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
    /// Subclass entry point: tear down the model server this run started
    /// (called from <see cref="DisposeAsync"/> after the translator
    /// provider is disposed).
    /// </summary>
    protected virtual ValueTask DisposeModelServerAsync()
    {
        return ValueTask.CompletedTask;
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
}

/// <summary>The pure ambient-env stamp step <see cref="RealPiHarnessBase"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class AmbientPiEnvironmentStamper
{
    /// <summary><c>PI_CODING_AGENT_DIR</c> — the directory real pi reads <c>models.json</c> from (issue #150's redirect mechanism).</summary>
    private const string AgentDirVariable = "PI_CODING_AGENT_DIR";

    /// <summary><c>ANTHROPIC_AUTH_TOKEN</c> — bearer header used during in-process runs (no paid API; standing in for the still-missing production stamp).</summary>
    private const string AuthTokenVariable = "ANTHROPIC_AUTH_TOKEN";

    /// <summary>Bearer-token value stamped for the duration of one run — opaque to pi, no live API behind it.</summary>
    private const string FakeAuthTokenValue = "real-pi-test-token";

    /// <summary>
    /// Stamps the two ambient pi env vars for the duration of one
    /// translator cycle and returns an <see cref="IDisposable"/> that
    /// restores the prior values on dispose — narrows the window a
    /// process-global env mutation is visible to any other collection
    /// running concurrently in the same test assembly. Stands in for
    /// the still-missing production Translator env stamp (see
    /// <see cref="RealPiFakeModelHarness"/>'s own remarks / WS7 report).
    /// </summary>
    public static IDisposable Stamp(string agentDirectory)
    {
        var previousAgentDir = Environment.GetEnvironmentVariable(AgentDirVariable);
        var previousAuthToken = Environment.GetEnvironmentVariable(AuthTokenVariable);
        Environment.SetEnvironmentVariable(AgentDirVariable, agentDirectory, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(AuthTokenVariable, FakeAuthTokenValue, EnvironmentVariableTarget.Process);

        return new RestoreOnDispose(previousAgentDir, previousAuthToken);
    }

    /// <summary>Restores the two ambient pi env vars <see cref="Stamp"/> overwrote.</summary>
    private sealed class RestoreOnDispose(string? previousAgentDir, string? previousAuthToken) : IDisposable
    {
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AgentDirVariable, previousAgentDir, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(AuthTokenVariable, previousAuthToken, EnvironmentVariableTarget.Process);
        }
    }
}

/// <summary>The pure <c>models.json</c> writer step <see cref="RealPiHarnessBase"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class PiModelsJsonWriter
{
    /// <summary>
    /// Writes the <c>providers.anthropic.baseUrl</c>-only <c>models.json</c>
    /// under <paramref name="agentDirectory"/> real pi reads from via
    /// <c>PI_CODING_AGENT_DIR</c>. Per pi's "Overriding Built-in
    /// Providers" rules the baseUrl override merges into the built-in
    /// catalog — every cataloged Claude model id stays selectable,
    /// just redirected. Shared by all three harness shapes
    /// (fake|replay|record) — only the baseUrl value differs per run.
    /// </summary>
    public static async Task WriteAsync(string agentDirectory, Uri modelBaseAddress, CancellationToken cancellationToken)
    {
        var modelsJson =
            /*lang=json,strict*/ $$"""
            {
              "providers": {
                "anthropic": {
                  "baseUrl": "{{modelBaseAddress}}"
                }
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(agentDirectory, "models.json"), modelsJson, cancellationToken);
    }
}
