using Comuki.Host.Brain.Ports;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Comuki.Shared.Contracts.Plans;
using Microsoft.Extensions.AI;

namespace Comuki.Host.Brain;

/// <summary>
/// Per-request tool surface of the brain agent loop: memory.search,
/// list_profiles, list_active_runs, read_explorer_report and emit_plan.
/// One toolbox per Think call — the emitted plan and the invalid-plan
/// counter are per-request state. Tool names match the S5 contract.
/// </summary>
public sealed class BrainToolbox(
    IMemoryStore memoryStore,
    IProfileCatalog profileCatalog,
    IActiveRunCatalog activeRuns,
    IExplorerReportReader explorerReports)
{
    /// <summary>How many invalid emit_plan attempts were tolerated before the hard error.</summary>
    public const int MaxInvalidPlanAttempts = 1;

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
                description: "Search long-term memory facts. Optional scope: user|project|global (default global); "
                    + "optional subject (the user/project id; default global). Returns kind, topic, text per fact."),

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

    /// <summary>memory.search — scope defaults to global; falls back to the embedding-free ranking.</summary>
    /// <param name="query"></param>
    /// <param name="scope"></param>
    /// <param name="subject"></param>
    /// <param name="limit"></param>
    public async Task<string> SearchMemoryAsync(
        string query,
        string? scope = null,
        string? subject = null,
        int? limit = null)
    {
        var parsedScope = scope is null
            ? MemoryScope.Global
            : MemoryScopeKeys.Parse(scope)
                ?? throw new ArgumentException($"unknown scope '{scope}' — expected user|project|global");
        var parsedSubject = string.IsNullOrWhiteSpace(subject) ? MemoryScopeKeys.GlobalSubject : subject;

        var facts = await memoryStore.SearchAsync(
            new MemoryFactQuery(
                Scope: parsedScope,
                SubjectId: parsedSubject,
                Limit: Math.Clamp(limit ?? 5, 1, 20)),
            CancellationToken.None);

        return facts.Count == 0
            ? $"no memory facts for '{query}'"
            : string.Join("\n", facts.Select(static fact =>
                $"[{MemoryToolsText.KindOf(fact)}] {fact.TopicKey}: {fact.Text}"));
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
