namespace Comuki.Host.Errors;

/// <summary>
/// The neutral wire row an <see cref="IProblemHandler"/> produces: everything
/// the composition-root <see cref="ProviderExceptionHandler"/> needs to execute
/// one RFC 9457 response, before any ASP.NET type is touched. Handlers stay
/// unit-testable against this record — no <c>HttpContext</c> plumbing in them.
/// </summary>
/// <param name="StatusCode">HTTP status the response carries.</param>
/// <param name="Type">Stable URN for the error class (<c>urn:comuki:error:{code}</c>; <c>about:blank</c> only for the unhandled 500).</param>
/// <param name="Title">Short, stable, human.</param>
/// <param name="Detail">Safe human detail — no stack, no secret, no PII.</param>
/// <param name="Code">Stable dot.case identifier — clients branch on it.</param>
/// <param name="Extensions">ProblemDetails extensions, always including <c>code</c> plus any per-domain keys.</param>
/// <param name="ValidationErrors">Non-null ⇒ the handler emits <c>TypedResults.ValidationProblem</c> (400) and the Problem-path fields are not serialized.</param>
public sealed record ProblemAnswer(
    int StatusCode,
    string Type,
    string Title,
    string Detail,
    string Code,
    IReadOnlyDictionary<string, object?> Extensions,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    /// <summary>
    /// Builds a row whose <paramref name="code"/> drives both the
    /// <c>type</c> URN and the <c>code</c> extension — the two can never
    /// disagree because neither is hand-written per handler. The
    /// <c>code</c> extension is injected first, then the caller's keys,
    /// matching the serialization order every module runner shipped.
    /// </summary>
    /// <param name="statusCode">HTTP status the response carries.</param>
    /// <param name="title">Short, stable, human.</param>
    /// <param name="detail">Safe human detail — no stack, no secret, no PII.</param>
    /// <param name="code">Stable dot.case identifier; the URN is derived from it.</param>
    /// <param name="extensions">Per-domain extensions beyond <c>code</c>, in serialization order.</param>
    /// <param name="validationErrors">Per-field failures; non-null turns the row into a 400 ValidationProblem.</param>
    /// <returns>The wire row.</returns>
    public static ProblemAnswer For(
        int statusCode,
        string title,
        string detail,
        string code,
        IReadOnlyDictionary<string, object?>? extensions = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        Dictionary<string, object?> rows = new() { ["code"] = code };

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                rows[key] = value;
            }
        }

        return new ProblemAnswer(
            statusCode,
            $"urn:comuki:error:{code}",
            title,
            detail,
            code,
            rows,
            validationErrors);
    }

    /// <summary>
    /// The 400 ValidationProblem row: only <see cref="StatusCode"/> and
    /// <see cref="ValidationErrors"/> are consumed — the composition root
    /// emits a bare <c>TypedResults.ValidationProblem(errors)</c>, the exact
    /// shape the per-module runners shipped.
    /// </summary>
    /// <param name="errors">Field name → that field's failure messages, ordinal-grouped.</param>
    /// <returns>The validation wire row.</returns>
    public static ProblemAnswer Validation(IReadOnlyDictionary<string, string[]> errors)
    {
        return new ProblemAnswer(
            StatusCodes.Status400BadRequest,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new Dictionary<string, object?>(),
            errors);
    }
}
