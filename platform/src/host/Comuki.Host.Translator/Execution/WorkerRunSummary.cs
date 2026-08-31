using System.Text;
using Comuki.Host.Translator.Parsing;

namespace Comuki.Host.Translator.Execution;

/// <summary>
/// Accumulates the assistant's output over one pi run: streaming text
/// deltas append; the authoritative <c>message_end</c> assistant text
/// replaces (pi guarantees it is the final wording). Feeds the
/// StageReport's result text.
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
                _ = text.Append(delta.Delta);
                break;
            case PiEvent.AssistantTextEvent authoritative:
                _ = text.Clear();
                _ = text.Append(authoritative.Text);
                break;
        }
    }

    /// <summary>The accumulated assistant text so far.</summary>
    public string ResultText => text.ToString();
}
