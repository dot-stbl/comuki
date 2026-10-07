using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Parsing;

namespace Comuki.Host.Translator.Execution.Loop;

/// <summary>
/// Side-effect pass on the worker run: tracks the harness's
/// agent-settled signal so the <c>WorkerCommandHandler</c>
/// chooses between <c>steer</c> and <c>follow_up</c>. Lives here
/// (not on <see cref="WorkerRunSummary"/>) because the run is what
/// the command handler reads, not the summary. <see cref="PiPump"/>
/// calls <see cref="Observe"/> from its pump loop; the helper is
/// <c>internal static</c> (not file-scoped) so <see cref="PiPump"/>
/// in the same project can reach it.
/// </summary>
internal static class PiRunStateObserver
{
    public static void Observe(WorkerRun run, PiEvent piEvent)
    {
        switch (piEvent)
        {
            case PiEvent.AgentSettledEvent:
                run.HasAgentSettled = true;
                break;
            case PiEvent.AgentStartEvent:
                run.HasAgentSettled = false;
                break;
        }
    }
}
