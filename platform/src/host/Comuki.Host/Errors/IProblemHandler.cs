namespace Comuki.Host.Errors;

/// <summary>
/// Maps one exact exception type onto its <see cref="ProblemAnswer"/> wire
/// row. One implementation per domain error type, collected by DI into the
/// <see cref="ProblemHandlerRegistry"/> — adding a domain error adds a
/// handler class and a registration line; no existing mapping file is edited
/// (typed registry per <c>variation-points.md</c>, not a switch).
/// </summary>
public interface IProblemHandler
{
    /// <summary>The exact exception type this handler answers for; the registry matches it first, then walks the base chain.</summary>
    public Type ExceptionType { get; }

    /// <summary>Builds the wire row for a thrown exception of <see cref="ExceptionType"/>.</summary>
    /// <param name="exception">The thrown exception; its runtime type is <see cref="ExceptionType"/>.</param>
    /// <returns>The row the composition root executes.</returns>
    public ProblemAnswer Answer(Exception exception);
}
