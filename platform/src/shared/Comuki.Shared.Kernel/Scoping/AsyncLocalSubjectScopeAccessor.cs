namespace Comuki.Shared.Kernel.Scoping;

/// <summary>
/// <see cref="ISubjectScopeAccessor"/> backed by
/// <see cref="AsyncLocal{T}"/>: the scope follows the asynchronous flow
/// across awaits and cannot leak between two requests or two worker
/// cycles running side by side.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton. The state lives in the
/// <see cref="AsyncLocal{T}"/>, not in the instance, so there is nothing
/// per-scope to hold — and a scoped registration would actively break
/// the mechanism: a worker sets the scope on the root instance, then
/// resolves its stores from a per-cycle child DI scope, which would hand
/// them a second accessor with an empty slot.
/// </para>
/// <para>
/// Two ways in: <see cref="AsSystem"/> names an unrestricted system
/// consumer (workers, migrators); <see cref="Begin"/> installs a subject
/// scope the establishing side has already resolved. The global query
/// filters read the result through the context scope members; neither
/// method is called from inside a query.
/// </para>
/// </remarks>
public sealed class AsyncLocalSubjectScopeAccessor : ISubjectScopeAccessor
{
    private readonly AsyncLocal<SubjectScope?> current = new();

    /// <inheritdoc />
    public SubjectScope Current => current.Value
        ?? throw new InvalidOperationException(
            "No subject scope is established on this flow. A request path must carry an "
            + "authenticated subject; a background consumer must declare itself with "
            + "ISubjectScopeAccessor.AsSystem(\"<consumer-name>\"). There is no default scope — "
            + "an empty one would silently return zero rows, an unrestricted one would "
            + "fail open.");

    /// <inheritdoc />
    public SubjectScope? CurrentOrNone => current.Value;

    /// <inheritdoc />
    public IDisposable AsSystem(string consumerName)
    {
        var previous = current.Value;
        current.Value = SubjectScope.ForSystem(consumerName);

        return new SubjectScopeRestore(current, previous);
    }

    /// <inheritdoc />
    public IDisposable Begin(SubjectScope scope)
    {
        var previous = current.Value;
        current.Value = scope;

        return new SubjectScopeRestore(current, previous);
    }
}

/// <summary>
/// Restores the slot to what it held before the scope was entered —
/// including "nothing was established", which is why the previous value
/// is captured rather than cleared. Idempotent: a second
/// <c>Dispose</c> must not overwrite a scope entered after the first.
/// </summary>
/// <param name="slot">The accessor's ambient slot.</param>
/// <param name="previous">The value to put back, possibly null.</param>
file sealed class SubjectScopeRestore(AsyncLocal<SubjectScope?> slot, SubjectScope? previous) : IDisposable
{
    private bool disposed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        slot.Value = previous;
    }
}
