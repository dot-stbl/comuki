using System.Runtime.CompilerServices;
using Comuki.Platform.Worker.Translator.Interfaces;
using Comuki.Platform.Worker.Translator.Services;

namespace Comuki.Platform.Worker.Translator.Services;

/// <summary>
/// Default <see cref="ITranslator"/> implementation. Pipes <see cref="IPiRunner"/>
/// stdout through <see cref="StreamJsonParser"/> to produce typed
/// <see cref="PiEvent"/>s. Stateless: each call is independent.
/// </summary>
/// <remarks>
/// Lives in the <c>.Services</c> sub-namespace so the type name
/// (<c>WorkerTranslator</c>) doesn't collide with the project-root namespace
/// <c>Comuki.Platform.Worker.Translator</c> (MA0049).
/// </remarks>
public sealed class WorkerTranslator : ITranslator
{
    private readonly IPiRunner runner;

    public WorkerTranslator(IPiRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        this.runner = runner;
    }

    public async IAsyncEnumerable<PiEvent> TranslateAsync(
        string brief,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(brief);

        await foreach (var line in runner.RunAsync(brief, cancellationToken).ConfigureAwait(false))
        {
            foreach (var piEvent in StreamJsonParser.ParseLine(line))
            {
                yield return piEvent;
            }
        }
    }
}
