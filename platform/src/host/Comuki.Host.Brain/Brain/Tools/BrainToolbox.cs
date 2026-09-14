using Comuki.Host.Brain.Brain.Exceptions;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Comuki.Shared.Contracts.Plans;
using Microsoft.Extensions.AI;

namespace Comuki.Host.Brain.Brain.Tools;

/// <summary>
/// Per-request tool surface of the brain agent loop: memory.search,
/// memory.write, memory.forget, list_profiles, list_active_runs,
/// read_explorer_report and emit_plan. One toolbox per Think call — the
/// emitted plan and the invalid-plan counter are per-request state. Tool
/// names match the S5 contract (the memory pair extends it).
/// </summary>
/// <param name="memoryStore">Memory store behind the memory.* tools.</param>
/// <param name="clock">The write-time clock (custom ephemeral TTLs backdate created_at against it).</param>
/// <param name="profileCatalog">Worker profile catalog behind list_profiles.</param>
/// <param name="activeRuns">Active-run catalog behind list_active_runs.</param>
/// <param name="explorerReports">Explorer report reader behind read_explorer_report.</param>
/// <param name="embedder">
/// Optional embedding client — the same provider the knowledge module
/// uses. Present ⇒ memory.write embeds the fact text and memory.search
/// runs the cosine path; absent, unconfigured (noop) or failing ⇒ both
/// degrade to the embedding-free fallback ranking.
/// </param>
public sealed class BrainToolbox(
    IMemoryStore memoryStore,
    TimeProvider clock,
    IProfileCatalog profileCatalog,
    IActiveRunCatalog activeRuns,
    IExplorerReportReader explorerReports,
    IEmbeddingClient? embedder = null)
{
    /// <summary>How many invalid emit_plan attempts were tolerated before the hard error.</summary>
    public const int MaxInvalidPlanAttempts = 1;

    /// <summary>The created_by label the brain signs its writes with.</summary>
    public const string WriteActor = "brain";

    /// <summary>How many facts memory.forget scans for a topic match.</summary>
    public const int ForgetScanLimit = 500;

    private string? emittedPlanJson;
    private int invalidPlanAttempts;
    private IReadOnlyList<AIFunction> functions = [];

    /// <summary>
    /// Builds the AIFunction surface handed to the model — call once per
    /// think run, before the first round-trip. (A property initializer
    /// cannot bind instance methods, so this is a method.)
    /// </summary>
    public IReadOnlyList<AIFunction> BuildFunctions()
    {
        return functions =
        [
            AIFunctionFactory.Create(SearchMemoryAsync, name: "memory.search",
                description: "Search long-term shared memory facts (the platform-wide corpus — per-project "
                    + "and per-user recall is not exposed to this tool). Returns kind, topic, text per fact."),

            AIFunctionFactory.Create(WriteMemoryAsync, name: "memory.write",
                description: "Save a durable fact worth remembering across sessions, under a short canonical "
                    + "topic key (same topic overwrites). Use sparingly — only decisions, preferences and "
                    + "architectural constraints; NOT for transient task context. kind: 'standing' (permanent) "
                    + "or 'ephemeral' (expires; optional ttlHours caps its lifetime)."),

            AIFunctionFactory.Create(ForgetMemoryAsync, name: "memory.forget",
                description: "Forget the remembered fact stored under a topic key — use when a decision or "
                    + "preference it captured is obsolete or was corrected."),

            AIFunctionFactory.Create(ListProfilesAsync, name: "list_profiles",
                description: "List the worker profile catalog: key, name, description, allowed tools."),

            AIFunctionFactory.Create(ListActiveRunsAsync, name: "list_active_runs",
                description: "List currently active runs with their project and status."),

            AIFunctionFactory.Create(ReadExplorerReportAsync, name: "read_explorer_report",
                description: "Read the latest explorer (read-only recon) report, when one exists."),

            AIFunctionFactory.Create(EmitPlanAsync, name: "emit_plan",
                description: "Submit the final plan as JSON (shape from the system prompt). "
                    + "A valid plan ends the loop; an invalid one returns errors for one retry."),
        ];
    }

    /// <summary>Resolves a built function by tool name.</summary>
    /// <param name="name"></param>
    public AIFunction? FindFunction(string name)
    {
        return functions.FirstOrDefault(function => function.Name == name);
    }

    /// <summary>True when a valid plan was emitted; consumes it exactly once.</summary>
    /// <param name="planJson"></param>
    public bool TryConsumeEmittedPlan(out string planJson)
    {
        if (emittedPlanJson is { } captured)
        {
            planJson = captured;
            emittedPlanJson = null;
            return true;
        }

        planJson = string.Empty;
        return false;
    }

    /// <summary>
    /// memory.search — the shared global corpus only; embeds the query when
    /// a real embedding model is configured and falls back to the
    /// embedding-free ranking otherwise.
    /// </summary>
    /// <param name="query"></param>
    /// <param name="limit"></param>
    /// <remarks>
    /// This tool used to take a free <c>scope</c>/<c>subject</c> pair the
    /// model itself supplied — a tool-call argument, not an authenticated
    /// request. <see cref="BrainAgent"/> establishes no per-call project
    /// or subject scope (there is none to establish:
    /// <see cref="BrainRequest"/> carries no
    /// caller identity), so honoring an arbitrary
    /// <see cref="MemoryScope.Project"/>/<see cref="MemoryScope.User"/>
    /// value there would have let the model — or content that talks it
    /// into this via prompt injection — read back another project's or
    /// another person's facts as text. Rather than trust and gate that
    /// argument at runtime, the signature no longer exposes it: only
    /// <see cref="MemoryScope.Global"/> (the shared corpus, not owned by
    /// any one project or person) is reachable through this tool at all —
    /// the unsafe call is unrepresentable, not merely refused. The memory
    /// WRITE and FORGET tools below share exactly this posture. Reintroduce
    /// a scope parameter deliberately once a real per-call scope reaches
    /// the brain; project/user-specific recall in the meantime still
    /// reaches the brain safely through the pre-built digest context
    /// (<c>MemoryDigest</c>), assembled by a trusted caller that knows the
    /// real scope, not by these tools.
    /// </remarks>
    public async Task<string> SearchMemoryAsync(string query, int? limit = null)
    {
        var facts = await memoryStore.SearchAsync(
            new MemoryFactQuery(
                Scope: MemoryScope.Global,
                SubjectId: MemoryScopeKeys.GlobalSubject,
                Embedding: await MemoryToolEmbeddings.TryEmbedAsync(embedder, query),
                Limit: Math.Clamp(limit ?? 5, 1, 20)),
            CancellationToken.None);

        return facts.Count == 0
            ? $"no memory facts for '{query}'"
            : string.Join("\n", facts.Select(static fact =>
                $"[{MemoryToolsText.KindOf(fact)}] {fact.TopicKey}: {fact.Text}"));
    }

    /// <summary>
    /// memory.write — saves one fact into the shared global corpus as the
    /// brain (system consumer). Same-topic writes supersede; a custom
    /// ttlHours on an ephemeral fact backdates <c>created_at</c> so the
    /// fixed-horizon sweep expires it on schedule. The fact text is
    /// embedded when a real embedding model is configured so the cosine
    /// search path can find it later.
    /// </summary>
    /// <param name="topicKey">Short canonical topic — same topic overwrites.</param>
    /// <param name="text">The fact text.</param>
    /// <param name="kind">standing | ephemeral.</param>
    /// <param name="ttlHours">Optional lifetime for ephemeral facts; 1 hour minimum.</param>
    public async Task<string> WriteMemoryAsync(string topicKey, string text, string kind, int? ttlHours = null)
    {
        if (MemoryFactKindKeys.Parse(kind) is not { } parsedKind)
        {
            return $"memory.write rejected: kind must be '{MemoryFactKindKeys.Standing}' or '{MemoryFactKindKeys.Ephemeral}'";
        }

        if (ttlHours is { } && parsedKind != MemoryFactKind.Ephemeral)
        {
            return $"memory.write rejected: ttlHours applies to '{MemoryFactKindKeys.Ephemeral}' facts only";
        }

        if (ttlHours is <= 0)
        {
            return "memory.write rejected: ttlHours must be at least 1";
        }

        var now = clock.GetUtcNow();
        var written = await memoryStore.WriteAsync(
            new MemoryFactWrite(
                Scope: MemoryScope.Global,
                SubjectId: MemoryScopeKeys.GlobalSubject,
                Kind: parsedKind,
                TopicKey: topicKey,
                Text: text,
                Source: MemorySource.Chat,
                CreatedBy: WriteActor,
                Embedding: await MemoryToolEmbeddings.TryEmbedAsync(embedder, text),
                CreatedAt: ttlHours is { } lifetime
                    ? MemoryFactPolicy.EphemeralCreatedAt(now, TimeSpan.FromHours(lifetime))
                    : null),
            CancellationToken.None);

        return ttlHours is { } setHours
            ? $"remembered '{written.TopicKey}' ({MemoryFactKindKeys.Key(written.Kind)}, expires in ~{setHours}h)"
            : $"remembered '{written.TopicKey}' ({MemoryFactKindKeys.Key(written.Kind)})";
    }

    /// <summary>
    /// memory.forget — deletes the active fact stored under a topic key in
    /// the shared global corpus (the store's ForgetAsync; supersede
    /// history of already-superseded rows is not resurrected).
    /// </summary>
    /// <param name="topicKey">The topic to forget; canonicalized before matching.</param>
    public async Task<string> ForgetMemoryAsync(string topicKey)
    {
        var topic = MemoryFact.CanonicalKey(topicKey);
        var candidates = await memoryStore.ListAsync(
            MemoryScope.Global,
            MemoryScopeKeys.GlobalSubject,
            ForgetScanLimit,
            0,
            CancellationToken.None);

        var forgotten = 0;
        foreach (var fact in candidates.Where(fact => fact.TopicKey == topic))
        {
            if (await memoryStore.ForgetAsync(fact.Id, CancellationToken.None))
            {
                forgotten++;
            }
        }

        return forgotten == 0
            ? $"no memory fact under '{topic}'"
            : $"forgot '{topic}'";
    }

    /// <summary>list_profiles — the catalog the plan's profileKeys must come from.</summary>
    public async Task<string> ListProfilesAsync()
    {
        var profiles = await profileCatalog.ListAsync(CancellationToken.None);
        return profiles.Count == 0
            ? "profile catalog is empty"
            : string.Join("\n", profiles.Select(static profile =>
                $"{profile.Key} — {profile.Name}: {profile.Description}"));
    }

    /// <summary>list_active_runs — the stub returns none until the read API lands.</summary>
    public async Task<string> ListActiveRunsAsync()
    {
        var runs = await activeRuns.ListAsync(CancellationToken.None);
        return runs.Count == 0
            ? "no active runs"
            : string.Join("\n", runs.Select(static run =>
                $"{run.RunId} ({run.ProjectSlug}): {run.Status} since {run.StartedAt:O}"));
    }

    /// <summary>read_explorer_report — the stub reports absence until the journal surface lands.</summary>
    public async Task<string> ReadExplorerReportAsync()
    {
        return await explorerReports.ReadLatestAsync(CancellationToken.None) is { } report
            ? report
            : "no explorer report available";
    }

    /// <summary>
    /// emit_plan — validates the plan (shape, DAG, profile keys against
    /// the catalog) and captures it; the first invalid attempt feeds the
    /// errors back for the model's retry, the second fails the brain call.
    /// </summary>
    /// <param name="planJson"></param>
    public async Task<string> EmitPlanAsync(string planJson)
    {
        List<string> errors;
        if (PlanJson.TryParse(planJson, out var plan, out var validation))
        {
            var profiles = await profileCatalog.ListAsync(CancellationToken.None);
            var knownKeys = profiles.Select(static profile => profile.Key).ToHashSet(StringComparer.Ordinal);
            errors =
            [
                .. plan.Nodes
                    .Select(static node => node.ProfileKey)
                    .Where(key => !knownKeys.Contains(key))
                    .Distinct()
                    .Select(key => $"node profile key '{key}' is not in the profile catalog (run list_profiles)"),
            ];
        }
        else
        {
            errors = [.. validation.Errors];
        }

        if (errors.Count == 0)
        {
            emittedPlanJson = planJson;
            return "plan accepted";
        }

        invalidPlanAttempts++;
        return invalidPlanAttempts <= MaxInvalidPlanAttempts
            ? "plan rejected — fix these errors and call emit_plan again:\n" + string.Join("\n", errors)
            : throw new BrainInvalidPlanException(errors);
    }
}

/// <summary>Formatting helpers for the tool outputs.</summary>
file static class MemoryToolsText
{
    public static string KindOf(MemoryFactView fact)
    {
        return MemoryFactKindKeys.Key(fact.Kind);
    }
}

/// <summary>
/// The embedding seam of the memory tools: embed when a real model is
/// configured, degrade silently otherwise. The noop provider means "no
/// model configured" — its deterministic junk vectors would cosine-rank
/// arbitrarily, so the fallback ranking is the better answer there.
/// Embedding is an accelerator, never a gate: a write lands without a
/// vector and a search falls back to ranking when the provider fails.
/// </summary>
file static class MemoryToolEmbeddings
{
    public static async Task<float[]?> TryEmbedAsync(IEmbeddingClient? embedder, string text)
    {
        if (embedder is null || embedder.ProviderName == EmbeddingProviderKindKeys.Noop)
        {
            return null;
        }

        try
        {
            return await embedder.EmbedAsync(text);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // transport / auth / provider-rejected — same degradation as
            // "not configured": memory must keep working without embeddings
            return null;
        }
    }
}
