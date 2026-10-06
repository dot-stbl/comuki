using System.Text;
using Comuki.Host.Translator.Parsing;

namespace Comuki.Host.Translator.Execution.Run;

/// <summary>
/// Accumulates the assistant's output over one pi run: streaming text
/// deltas append; the authoritative <c>message_end</c> assistant text
/// replaces (pi guarantees it is the final wording). Feeds the
/// StageReport's result text.
/// <para>
/// The agent-settled signal is tracked separately on
/// <see cref="WorkerRun.HasAgentSettled"/> (the
/// <c>WorkerCommandHandler</c> reads it directly; the
/// <see cref="PiEvent.AgentSettledEvent"/> observation lives in
/// the pump's <c>ObserveRunState</c> pass to keep this class
/// focused on text accumulation).
/// </para>
/// </summary>
public sealed class WorkerRunSummary()
{
    private readonly StringBuilder text = new();

    /// <summary>Observes one pi event, folding text-producing events into the summary.</summary>
    /// <param name="piEvent"></param>
    public void Observe(PiEvent piEvent)
    {
        switch (piEvent)
        {
            case PiEvent.TextDeltaEvent delta:
                text.Append(delta.Delta);
                break;
            case PiEvent.AssistantTextEvent authoritative:
                text.Clear();
                text.Append(authoritative.Text);
                break;
        }
    }

    /// <summary>The accumulated assistant text so far.</summary>
    public string ResultText => text.ToString();
}
