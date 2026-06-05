using Comuki.Platform.Worker.Translator.Services;

namespace Comuki.Platform.Worker.Translator.Interfaces;

/// <summary>
/// Top-level contract for the Translator: takes a brief, spawns the worker agent
/// via <see cref="IPiRunner"/>, and yields typed <see cref="PiEvent"/>s as the
/// agent's stream-json output arrives.
///
/// No business state — each call to <see cref="TranslateAsync"/> is independent.
/// A hosted service is expected to call this in response to a task being claimed.
/// </summary>
public interface ITranslator
{
    /// <summary>
    /// Runs the agent on the given brief and yields typed events from its stream-json
    /// output. The first event is normally <see cref="PiEvent.SystemEvent"/>, the
    /// last is <see cref="PiEvent.ResultEvent"/>, with <see cref="PiEvent.AssistantTextEvent"/>
    /// and <see cref="PiEvent.AssistantToolUseEvent"/> in between.
    /// </summary>
    public IAsyncEnumerable<PiEvent> TranslateAsync(string brief, CancellationToken cancellationToken = default);
}
