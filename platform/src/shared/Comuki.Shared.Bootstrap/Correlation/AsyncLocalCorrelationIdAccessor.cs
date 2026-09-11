namespace Comuki.Shared.Bootstrap.Correlation;

/// <summary>
/// <see cref="ICorrelationIdAccessor"/> backed by
/// <see cref="AsyncLocal{T}"/> (issue #56 §5): the correlation id follows
/// the asynchronous flow across awaits and cannot leak between two
/// requests or two worker cycles running side by side. Registered as a
/// singleton — the state lives in the <see cref="AsyncLocal{T}"/> slot,
/// not in the instance. Mirrors AsyncLocalSubjectScopeAccessor without
/// sharing its slot: correlation and subject scope are separate concerns.
/// </summary>
public sealed class AsyncLocalCorrelationIdAccessor() : ICorrelationIdAccessor
{
    private readonly AsyncLocal<string?> current = new();

    /// <inheritdoc />
    public string? CurrentId => current.Value;

    /// <inheritdoc />
    public IDisposable Begin(string requestId)
    {
        var previous = current.Value;
        current.Value = requestId;

        return new CorrelationIdRestore(current, previous);
    }
}

/// <summary>
/// Restores the slot to what it held before the correlation id was
/// entered — including "nothing was established", which is why the
/// previous value is captured rather than cleared. Idempotent: a second
/// <c>Dispose</c> must not overwrite an id entered after the first.
/// </summary>
/// <param name="slot">The accessor's ambient slot.</param>
/// <param name="previous">The value to put back, possibly null.</param>
file sealed class CorrelationIdRestore(AsyncLocal<string?> slot, string? previous) : IDisposable
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
