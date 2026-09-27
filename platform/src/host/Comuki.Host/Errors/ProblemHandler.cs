namespace Comuki.Host.Errors;

/// <summary>
/// Boilerplate base for per-error-type handlers: fixes
/// <see cref="ExceptionType"/> to <typeparamref name="TException"/> and
/// narrows the answer to the typed exception. Subclasses pass their fixed
/// status and title here and build rows through <see cref="Row"/>, whose URN
/// is derived from the code — status, title and code spelling live in exactly
/// one place per error class.
/// </summary>
/// <typeparam name="TException">The exact exception type this handler answers for.</typeparam>
/// <param name="statusCode">HTTP status every row of this error class carries.</param>
/// <param name="title">Stable, human title every row of this error class carries.</param>
public abstract class ProblemHandler<TException>(int statusCode, string title) : IProblemHandler
    where TException : Exception
{
    /// <inheritdoc />
    public Type ExceptionType => typeof(TException);

    /// <inheritdoc />
    public ProblemAnswer Answer(Exception exception)
    {
        return Answer((TException)exception);
    }

    /// <summary>Builds the row for this handler's fixed status + title, deriving the URN from <paramref name="code"/>.</summary>
    /// <param name="detail">Safe human detail — no stack, no secret, no PII.</param>
    /// <param name="code">Stable dot.case machine code; drives both the URN and the <c>code</c> extension.</param>
    /// <param name="extensions">Per-domain extensions beyond <c>code</c>, in serialization order.</param>
    /// <returns>The wire row.</returns>
    protected ProblemAnswer Row(string detail, string code, IReadOnlyDictionary<string, object?>? extensions = null)
    {
        return ProblemAnswer.For(statusCode, title, detail, code, extensions);
    }

    /// <summary>Builds the wire row for the typed exception.</summary>
    /// <param name="exception">The thrown exception, already narrowed to <typeparamref name="TException"/>.</param>
    /// <returns>The row the composition root executes.</returns>
    protected abstract ProblemAnswer Answer(TException exception);
}
