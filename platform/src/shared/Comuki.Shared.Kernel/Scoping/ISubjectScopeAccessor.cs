namespace Comuki.Shared.Kernel.Scoping;

/// <summary>
/// Reads the <see cref="SubjectScope"/> in force on the current
/// asynchronous flow, and lets a consumer without a subject — a
/// background worker, a migrator — declare itself a system consumer for
/// the duration of a <c>using</c> block.
/// </summary>
/// <remarks>
/// <para>
/// The scope is ambient on purpose: the code that needs it (a row-level
/// query filter) sits far below the code that knows it (authentication,
/// or a worker's own loop), and threading a parameter through every
/// store is exactly the plumbing this port exists to avoid.
/// </para>
/// <para>
/// <b>Where to call <see cref="AsSystem"/>.</b> Always in a
/// <c>using</c>, so the scope is left on the same flow that entered
/// it. That is the whole discipline: an <c>async</c> method cannot push
/// the scope onto its caller — the state machine restores the caller's
/// execution context — and a synchronous shape sets and restores in the
/// same breath. What is never allowed is entering the scope and
/// returning without disposing it.
/// </para>
/// </remarks>
public interface ISubjectScopeAccessor
{
    /// <summary>
    /// The scope in force, never null. <b>Throws</b> when no scope has
    /// been established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is deliberately no default, in either direction.
    /// </para>
    /// <para>
    /// An empty scope as the default would mean a background worker that
    /// forgot to declare itself silently sees zero rows: the queue stops
    /// shipping work and nothing reports a fault, because a query
    /// returning no rows is a legitimate result.
    /// </para>
    /// <para>
    /// An unrestricted scope as the default would mean fail-open on every
    /// path that forgot to establish a subject — the one failure mode a
    /// row-level filter exists to prevent.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No scope is established on this flow.</exception>
    public SubjectScope Current { get; }

    /// <summary>
    /// The scope in force, or null when none is established. Bookkeeping
    /// only — never a query filter, never an access decision; anything
    /// that decides what a caller may see reads <see cref="Current"/> and
    /// lets it throw.
    /// </summary>
    public SubjectScope? CurrentOrNone { get; }

    /// <summary>
    /// Enters an unrestricted system scope named
    /// <paramref name="consumerName"/> and returns the handle that
    /// leaves it. Disposal restores the previous scope, including the
    /// "none established" state; nesting works, and disposing twice is a
    /// no-op.
    /// </summary>
    /// <param name="consumerName">
    /// The consumer's kebab-case name (<c>lease-reaper</c>,
    /// <c>worker-runtime</c>) — it names the consumer, not the
    /// operation, so an audit read can attribute the work.
    /// </param>
    public IDisposable AsSystem(string consumerName);

    /// <summary>
    /// Installs the given — possibly restricted — subject scope and
    /// returns the handle that leaves it. The establishing side
    /// (authentication middleware, a permission evaluator) resolves the
    /// subject's assignments into the finished <see cref="SubjectScope"/>
    /// <b>before</b> calling this; the query filters only read the
    /// ambient value, because an <c>await</c> cannot live inside a query
    /// predicate. Disposal restores the previous scope, including the
    /// "none established" state; nesting works, and disposing twice is a
    /// no-op.
    /// </summary>
    /// <param name="scope">The scope in force until the returned handle is disposed.</param>
    public IDisposable Begin(SubjectScope scope);
}
