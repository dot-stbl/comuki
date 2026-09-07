namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// One golden task as it lives on disk: a stable id, a human-readable
/// name, the aggregate it drives and the sequence of operations the
/// runner replays against that aggregate. The expected outcome lives
/// on <see cref="Expected"/>.
/// </summary>
/// <param name="Id">Stable identifier — used as the report filename stem.</param>
/// <param name="Name">Human-readable name for the markdown report.</param>
/// <param name="Kind">Which aggregate the runner drives.</param>
/// <param name="Operations">Steps the runner applies, in order.</param>
/// <param name="Expected">Expected outcome — diffed by the runner.</param>
public sealed record EvalTask(
    string Id,
    string Name,
    EvalTaskKind Kind,
    IReadOnlyList<EvalOperation> Operations,
    EvalExpected Expected);