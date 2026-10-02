namespace Comuki.Host.Translator.Execution.Run;

/// <summary>
/// Run-scoped artifact list the Translator accumulates during pi's
/// lifetime (harden-pi-worker-sandbox 5.2, spec D7). The Translator's
/// <c>WorkerCommandHandler</c> calls <see cref="Add"/> when the
/// worker SDK drops a pin / artifact; the loop reads <see cref="Snapshot"/>
/// and ships it as a <c>StageDrain</c> event before <c>complete</c> /
/// <c>fail</c>. A drain failure does not block completion — the
/// artifact list is a hint to the host packager, not a gate.
/// </summary>
/// <remarks>
/// The accumulator is single-threaded (the loop is the only writer),
/// so <see cref="List{T}"/> + <c>Add</c> from one place does not need
/// a lock. Workers today do not push artifacts yet, so the list is
/// usually empty; the drain path still runs and the host journal
/// records a <c>worker.drained</c> entry with the (empty) list.
/// </remarks>
public sealed class ArtifactAccumulator
{
    /// <summary>backing store — single-writer, the Translator loop.</summary>
    private readonly List<string> artifacts = [];

    /// <summary>
    /// Records one artifact name. Called from <see cref="Commands.WorkerCommandHandler"/>
    /// when the worker SDK reports a pin / artifact via the gRPC
    /// command surface (no command today, the API is reserved for slice 6
    /// follow-up: pi-extension surface).
    /// </summary>
    /// <param name="name">Object name (relative path) under the run prefix.</param>
    public void Add(string name)
    {
        artifacts.Add(name);
    }

    /// <summary>
    /// Read-only snapshot of every artifact recorded so far. The loop
    /// ships this as the <c>StageDrain.Artifacts</c> list and resets
    /// after — calling <see cref="Snapshot"/> again returns the empty
    /// list.
    /// </summary>
    /// <returns>Immutable snapshot, safe to ship over the wire.</returns>
    public IReadOnlyList<string> Snapshot()
    {
        return [.. artifacts];
    }
}
