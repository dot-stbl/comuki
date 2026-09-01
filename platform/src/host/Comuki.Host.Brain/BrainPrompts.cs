using Comuki.Shared.Contracts.Brain;

namespace Comuki.Host.Brain;

/// <summary>
/// System prompts per brain request kind. The plan prompt pins the plan
/// JSON shape and the emit_plan protocol — everything else stays short
/// and task-shaped.
/// </summary>
public static class BrainPrompts
{
    /// <summary>The plan-JSON sketch the plan prompt hands to the model.</summary>
    public const string PlanSchemaHint =
                             /*lang=json,strict*/
                             """
        {
          "summary": "what the plan accomplishes",
          "nodes": [
            { "id": "n1", "title": "step title", "profileKey": "implement", "brief": "worker brief" }
          ],
          "edges": [ { "from": "n1", "to": "n2" } ]
        }
        """;

    /// <summary>System prompt for plan decomposition.</summary>
    public const string Plan =
        """
        You are the Comuki brain: you decompose tasks into worker plans.
        Use the tools to ground yourself: memory.search for remembered facts,
        list_profiles for the worker profile catalog, list_active_runs and
        read_explorer_report for run context. When the plan is ready you MUST
        call emit_plan with a JSON document of this exact shape:
        """ + "\n" + PlanSchemaHint + """

        Rules: every node id is unique and referenced edges exist; the graph
        is acyclic; every node names a real profile key from the catalog;
        every brief is self-contained (the worker sees only its brief).
        An invalid plan is rejected with errors — fix them and call
        emit_plan again.
        """;

    /// <summary>System prompt for brief writing.</summary>
    public const string Brief =
        """
        You are the Comuki brain: you write one self-contained worker brief.
        Ground it with memory.search / read_explorer_report when useful.
        The worker sees only your brief — include the file paths, commands
        and constraints it needs; no references to other steps.
        """;

    /// <summary>System prompt for repair suggestions.</summary>
    public const string Repair =
        """
        You are the Comuki brain: you repair a failing work item from its
        report. Diagnose with memory.search / read_explorer_report /
        list_active_runs when useful; answer with the corrected brief for
        the same step.
        """;

    /// <summary>System prompt for plain answers.</summary>
    public const string Answer =
        """
        You are the Comuki brain: you answer questions about the platform —
        projects, runs, profiles, remembered facts. Use the tools instead of
        guessing; say what you do not know.
        """;

    /// <summary>Picks the system prompt for a validated request kind key.</summary>
    /// <param name="kindKey"></param>
    public static string For(string kindKey)
    {
        return kindKey switch
        {
            BrainRequestKindKeys.Plan => Plan,
            BrainRequestKindKeys.Brief => Brief,
            BrainRequestKindKeys.Repair => Repair,
            BrainRequestKindKeys.Answer => Answer,
            _ => throw new ArgumentOutOfRangeException(nameof(kindKey), kindKey, null),
        };
    }
}
