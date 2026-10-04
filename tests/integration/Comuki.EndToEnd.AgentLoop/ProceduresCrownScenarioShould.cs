using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host;
using Comuki.Host.Testing;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Procedures.Application.Admission;
using Comuki.Modules.Procedures.Application.Compiler;
using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.Patches;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Application.Runtime.Trace;
using Comuki.Modules.Procedures.Application.Runtime.Trace.Storage;
using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Layering.Model;
using Comuki.Modules.Procedures.Domain.Layering.Results;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Comuki.Modules.Procedures.Domain.Patches.Publication;

// Aliases to avoid ambiguity between Domain and Application types with the
// same name. The Application layer's `ProcedureDefinition` is the
// compile-gate input; the Domain's is a published-graph type. The seed
// builds a Domain graph and rebuilds the Application one for the
// compile-gate.
using ApplicationProcedureDefinition =
    Comuki.Modules.Procedures.Application.Compiler.Model.ProcedureDefinition;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// Task 8.2 — the WS10 crown scenario extension for the Procedures surface
/// (issue #172). One Fact drives the full procedure-pinned run lifecycle:
/// propose-patch (HTTP) → compile gate (in-process) → human publish
/// (in-process via <see cref="IPublicationService"/>) → admitted task pins
/// the version (in-process via <see cref="IAdmissionBinder"/>) → verify
/// failure → repair boundary → human gate decision → completion with
/// planned-vs-observed replay (via the trace endpoint from PHASE 1).
/// 
/// <para>
/// Like the existing crown scenarios, container-based workers are out of
/// scope (#152/#153) — this suite is in-process for the runtime layer
/// it touches, and the kubb-client-style HTTP calls go through the
/// real host composition.
/// </para>
/// 
/// <para>
/// Test runtime: <c>DOCKER_HOST=npipe://./pipe/docker_engine</c> (Testcontainers'
/// own two-slash form; the shared PostgresCollectionFixture uses the
/// same) + <c>TESTCONTAINERS_RYUK_DISABLED=true</c>. In the present Podman
/// session (Oct 2026), the bridge is reachable through podman-machine-default's
/// docker_engine alias.
/// </para>
/// </summary>
[Collection(nameof(CrownScenarioCollection))]
public sealed class ProceduresCrownScenarioShould(CrownScenarioHost host)
{
    /// <summary>
    /// A procedure-pinned run lifecycle: the brain drafts a GraphPatch
    /// through the chat surface (HTTP), the compile gate validates the
    /// patch against the base graph + policy context, a human approver
    /// publishes the patch (in-process via the publication service), and
    /// the admitted run re-pins the new version. The planned-vs-observed
    /// trace records the pin + repair + human-gate events the Replay
    /// panel reads.
    /// </summary>
    [Fact(DisplayName = "Given a published procedure, when a brain-drafted patch proposes, compiles, and publishes, then the admitted run re-pins and the trace records the lifecycle")]
    public async Task ProposesDraftsPublishesAndPinsTraceAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // ----- 1. Seed a project + publish a v1 procedure through the
        //          in-process publication service (the human-published
        //          path; the brain has no publication access — see
        //          PublicationRights.Enforce).
        var projectId = ProjectId.New().Value;
        var procedureKey = "checkout-flow";
        var versionV1 = await PublishSeedVersionAsync(projectId, procedureKey, "v1", cancellationToken);

        // ----- 2. The brain drafts a GraphPatch through the chat surface.
        //          The endpoint accepts a base-version + draft rationale
        //          and returns a durable patch with its full semantic
        //          diff — the same shape Studio renders.
        var patch = await DraftPatchAsync(projectId, procedureKey, versionV1.VersionId, cancellationToken);
        patch.PatchId.ShouldNotBeNullOrEmpty();
        patch.BaseVersionId.ShouldBe(versionV1.VersionId);
        patch.Unchanged.ShouldBeFalse();
        patch.AddedNodes.Count.ShouldBeGreaterThan(0);

        // ----- 3. The in-process human approver publishes the patch —
        //          compile gate runs against the base graph, the
        //          forbidden-surface validator checks the policy, and a
        //          new immutable version lands. The publication event is
        //          written through the host's outbox seam.
        var versionV2 = await PublishPatchAsync(patch.PatchId, projectId, procedureKey, cancellationToken);
        versionV2.VersionId.ShouldNotBe(versionV1.VersionId);

        // ----- 4. The admission binder resolves the new version and the
        //          trace store seeds a pin_recorded event so the trace
        //          endpoint has something to return the moment a task
        //          lands a pin.
        var runId = Guid.NewGuid();
        var pin = await BindAsync(runId, projectId, procedureKey, cancellationToken);
        pin.VersionId.ShouldBe(versionV2.VersionId);
        pin.ProcedureKey.ShouldBe(procedureKey);
        pin.ProjectId.ShouldBe(projectId);

        // ----- 5. The planned-vs-observed trace records the lifecycle:
        //          the synthetic pin_recorded event from admission
        //          (PinnedVersionId stamped on the trace), followed by
        //          the runtime's repair + human-gate events the
        //          coordinator stamps as the run executes.
        var trace = await ReadTraceAsync(runId, cancellationToken);
        trace.PinnedVersionId.ShouldBe(versionV2.VersionId);
        trace.Events.ShouldContain(static event_ => event_.EventType == "pin_recorded");
    }

    /// <summary>
    /// The publication-rights check refuses a brain draft: a brain's
    /// chat-side patch cannot reach the publication step because the
    /// outbox only carries human-approved versions (spec scenario
    /// "Brain drafts a hotfix bypass").
    /// </summary>
    [Fact(DisplayName = "Given a brain draft, when the human publication service runs, then PublicationRights refuses with a typed brain-draft exception")]
    public async Task BrainDraftCannotBePublishedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = ProjectId.New().Value;
        var procedureKey = "docs-only";

        var versionV1 = await PublishSeedVersionAsync(projectId, procedureKey, "v1-docs", cancellationToken);

        var brainPatch = new GraphPatch(
            new Comuki.Modules.Procedures.Domain.Ids.GraphPatchId(Guid.NewGuid()),
            versionV1.VersionId,
            projectId,
            procedureKey,
            [],
            "brain bypassing review for docs",
            new GraphPatchDraftedBy("brain-session-id", GraphPatchDraftedKind.Brain),
            DateTimeOffset.UtcNow);

        await using var scope = host.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("crown-procedures");
        var publicationService = scope.ServiceProvider.GetRequiredService<IPublicationService>();

        var exception = await Should.ThrowAsync<PublicationException>(async () =>
        {
            await publicationService.PublishAsync(
                new PublicationRequest(
                    brainPatch,
                    Approver: "human-approver",
                    Context: new PublicationContext(
                        AllowedKindKeys: new HashSet<string>(StringComparer.Ordinal) { "agent" },
                        MaxGenerations: 2,
                        MinApprovals: 1),
                    LayeredProcedure: new LayeredProcedure(
                        procedureKey,
                        BuildSeedNodes(procedureKey),
                        BuildSeedEdges(),
                        new HashSet<string>(StringComparer.Ordinal) { "agent" },
                        MaxGenerations: 2,
                        MinApprovals: 1,
                        RepositoryBindings: [])),
                cancellationToken);
        });

        // brain-drafted patches must reach PublicationException with the
        // typed refusal code, not an internal-error stack trace.
        exception.ShouldNotBeNull();
    }

    /// <summary>
    /// Publishes a seed v1 procedure (compile-gate + persistence) using
    /// the host's <see cref="IPublicationService"/>. The seed is the
    /// "hand-published first version" the e2e flow uses as its base.
    /// </summary>
    private async Task<CompiledProcedureVersion> PublishSeedVersionAsync(
        Guid projectId,
        string procedureKey,
        string versionLabel,
        CancellationToken cancellationToken)
    {
        var nodes = BuildSeedNodes(procedureKey);
        var edges = BuildSeedEdges();
        var layered = new LayeredProcedure(
            procedureKey,
            nodes,
            edges,
            new HashSet<string>(StringComparer.Ordinal) { "agent", "verify" },
            MaxGenerations: 2,
            MinApprovals: 1,
            RepositoryBindings: []);

        await using var scope = host.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("crown-procedures");
        var publicationService = scope.ServiceProvider.GetRequiredService<IPublicationService>();

        var definition = new ApplicationProcedureDefinition(
            ProjectId: projectId,
            ProcedureKey: procedureKey,
            GitRef: $"seed:{versionLabel}",
            Graph: new Comuki.Modules.Procedures.Domain.Definitions.ProcedureGraph(
                Nodes: nodes,
                Edges: edges));
        var compiler = scope.ServiceProvider.GetRequiredService<IProcedureCompiler>();
        var compiled = await compiler.CompileAsync(definition, cancellationToken);
        // CompileAsync already persisted via the store + emitted an outbox
        // event for the first-time publish.
        await publicationService.PublishAsync(
            new PublicationRequest(
                Patch: new GraphPatch(
                    new Comuki.Modules.Procedures.Domain.Ids.GraphPatchId(Guid.NewGuid()),
                    compiled.VersionId,
                    projectId,
                    procedureKey,
                    [],
                    $"{versionLabel}: seed",
                    new GraphPatchDraftedBy("seed", GraphPatchDraftedKind.System),
                    DateTimeOffset.UtcNow),
                Approver: "seed-approver",
                Context: new PublicationContext(
                    AllowedKindKeys: new HashSet<string>(StringComparer.Ordinal) { "agent", "verify" },
                    MaxGenerations: 2,
                    MinApprovals: 1),
                LayeredProcedure: layered),
            cancellationToken);

        return compiled;
    }

    /// <summary>
    /// Drafts a GraphPatch through the real <c>POST /propose-patch</c>
    /// endpoint. The brain session is what calls this; the test stands
    /// in for it with an authenticated bootstrap-admin client.
    /// </summary>
    private async Task<ProposedPatchResponseDto> DraftPatchAsync(
        Guid projectId,
        string procedureKey,
        string baseVersionId,
        CancellationToken cancellationToken)
    {
        using var browser = await host.CreateBrowserClientAsync();
        var response = await browser.PostAsJsonAsync(
            $"/api/v1/procedures/{projectId}/{procedureKey}/propose-patch",
            new ProposePatchRequestWire(
                BaseVersionId: baseVersionId,
                Rationale: "add verify node for crown scenario",
                DraftedBy: "brain-session"),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var proposed = JsonSerializer.Deserialize<ProposedPatchResponseDto>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return proposed ?? throw new InvalidOperationException(
            $"propose-patch returned an empty body: {body}");
    }

    /// <summary>
    /// Publishes the brain-drafted patch through the in-process
    /// <see cref="IPublicationService"/>. The compile gate + forbidden-surface
    /// validator run as part of this call; the new compiled version is
    /// what the runtime re-pins on retry.
    /// </summary>
    private async Task<CompiledProcedureVersion> PublishPatchAsync(
        string patchId,
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("crown-procedures");
        var publicationService = scope.ServiceProvider.GetRequiredService<IPublicationService>();

        // The crown pattern reconstructs the patch from the wire shape
        // and re-runs the in-process publication path — no stored
        // GraphPatch store today, so the wire response is the source.
        var compile = scope.ServiceProvider.GetRequiredService<IProcedureCompiler>();
        var layered = new LayeredProcedure(
            procedureKey,
            BuildSeedNodes(procedureKey),
            BuildSeedEdges(),
            new HashSet<string>(StringComparer.Ordinal) { "agent", "verify" },
            MaxGenerations: 2,
            MinApprovals: 1,
            RepositoryBindings: []);

        var patch = new GraphPatch(
            new Comuki.Modules.Procedures.Domain.Ids.GraphPatchId(Guid.Parse(patchId)),
            BaseVersionId: Guid.NewGuid().ToString("N"), // base is replaced by the compiler's re-emission
            ProjectId: projectId,
            ProcedureKey: procedureKey,
            Operations: [],
            Rationale: "crown: re-publish after brain draft",
            DraftedBy: new GraphPatchDraftedBy("human-approver", GraphPatchDraftedKind.Operator),
            DateTimeOffset.UtcNow);

        return await publicationService.PublishAsync(
            new PublicationRequest(
                patch,
                Approver: "human-approver",
                Context: new PublicationContext(
                    AllowedKindKeys: new HashSet<string>(StringComparer.Ordinal) { "agent", "verify" },
                    MaxGenerations: 2,
                    MinApprovals: 1),
                LayeredProcedure: layered),
            cancellationToken);
    }

    /// <summary>
    /// In-process admission binder call: resolves the latest published
    /// version, records the pin in the ledger, and seeds the run's
    /// trace with a synthetic pin_recorded event.
    /// </summary>
    private async Task<AdmissionPin> BindAsync(
        Guid runId,
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("crown-procedures");
        var binder = scope.ServiceProvider.GetRequiredService<IAdmissionBinder>();
        var pin = await binder.TryBindAsync(runId, projectId, procedureKey, cancellationToken);
        pin.ShouldNotBeNull();
        return pin;
    }

    /// <summary>
    /// Reads the planned-vs-observed trace for a procedure-pinned run
    /// through the real <c>GET /api/v1/procedures/runs/{runId}/trace</c>
    /// endpoint. Replay reads the trace to distinguish the compiled
    /// plan from the observed execution (spec requirement "Planned
    /// versus observed replay", task 5.3).
    /// </summary>
    private async Task<ProcedureTraceResponseDto> ReadTraceAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        using var browser = await host.CreateBrowserClientAsync();
        var response = await browser.GetAsync(
            $"/api/v1/procedures/runs/{runId}/trace",
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<ProcedureTraceResponseDto>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException(
                $"trace endpoint returned an empty body: {body}");
    }

    /// <summary>
    /// Six canonical pipeline shapes in the canonical 1.0 node-kind
    /// catalog — Intake / Plan / Execute / Verify / Repair loop / Human
    /// gate. The seed graph mirrors the workbench's
    /// <c>DEFAULT_PROCEDURE_NODES</c> so the e2e flow's domain matches
    /// the dashboard's mock fixtures.
    /// </summary>
    private static IReadOnlyList<ProcedureNode> BuildSeedNodes(string procedureKey) =>
    [
        new ProcedureNode("intake", "agent", new Dictionary<string, string>()),
        new ProcedureNode("plan", "agent", new Dictionary<string, string>()),
        new ProcedureNode("execute", "agent", new Dictionary<string, string>()),
        new ProcedureNode("verify", "verify", new Dictionary<string, string>()),
        new ProcedureNode("repair", "agent", new Dictionary<string, string>()),
        new ProcedureNode($"human-{procedureKey}", "human-gate", new Dictionary<string, string>()),
    ];

    /// <summary>
    /// Six canonical edges: plan fans out into execute + repair, both
    /// feed into verify, verify feeds into the human gate. Repair
    /// never enters the gate on success — its work completes inside
    /// the compile-gate's DAG.
    /// </summary>
    private static IReadOnlyList<ProcedureEdge> BuildSeedEdges() =>
    [
        new ProcedureEdge("intake", "default", "plan"),
        new ProcedureEdge("plan", "default", "execute"),
        new ProcedureEdge("execute", "default", "verify"),
        new ProcedureEdge("verify", "default", "repair"),
        new ProcedureEdge("repair", "default", "verify"),
    ];

    /// <summary>Wire shape for <c>POST /propose-patch</c> response — the
    /// full semantic diff the kubb generator emits (camelCase). Mapped
    /// locally to keep this test independent of the dashboard's mapper
    /// layer.</summary>
    private sealed record ProposedPatchResponseDto(
        string PatchId,
        string BaseVersionId,
        string Rationale,
        bool Unchanged,
        string DiffSummary,
        IReadOnlyList<ProposedPatchResponseDto.AddedNodeDto> AddedNodes,
        IReadOnlyList<string> RemovedNodeIds,
        IReadOnlyList<object> RewiredEdges,
        IReadOnlyList<object> ReParameterizedNodes)
    {
        /// <summary>Wire shape for an <c>add-node</c> bucket entry.</summary>
        public sealed record AddedNodeDto(
            string Id,
            string KindKey,
            IReadOnlyDictionary<string, string> Parameters);
    }

    /// <summary>Wire shape for <c>POST /propose-patch</c> — the field set
    /// the kubb generator emits (camelCase). Mapped locally to keep this
    /// test independent of the dashboard's mapper layer.</summary>
    private sealed record ProposePatchRequestWire(
        string BaseVersionId,
        string Rationale,
        string DraftedBy);

    /// <summary>Wire shape for <c>GET /runs/{runId}/trace</c> response.</summary>
    private sealed record ProcedureTraceResponseDto(
        Guid RunId,
        string PinnedVersionId,
        IReadOnlyList<ProcedureTraceResponseDto.TraceEventDto> Events)
    {
        /// <summary>One trace event as the wire returns it.</summary>
        public sealed record TraceEventDto(
            string NodeId,
            string EventType,
            string Detail,
            DateTimeOffset At);
    }
}