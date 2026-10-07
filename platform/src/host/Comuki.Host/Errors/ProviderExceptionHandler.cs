using Microsoft.AspNetCore.Diagnostics;

namespace Comuki.Host.Errors;

/// <summary>
/// The single composition-root <see cref="IExceptionHandler"/>: typed
/// exceptions → <c>application/problem+json</c> per
/// <c>error-mapping.md</c> §4. It resolves one
/// <see cref="ProblemAnswer"/> through the <see cref="ProblemHandlerRegistry"/>
/// (exact type first, then base chain, kernel defaults last) and executes it
/// — the one place that decides what a thrown exception means on the wire.
/// Endpoints stay clean of error plumbing; per-error-type rows live in
/// handler classes, not in catch blocks. The per-module runner era this
/// class's doc comment once blessed ("until PR #20") is retired: every
/// module surface (Projects, Integrations, Scheduler, Chat, Runs, Learning) now
/// registers its rows via <c>Add&lt;Module&gt;ProblemHandlers()</c> and
/// throws straight through.
/// </summary>
/// <param name="registry">Typed registry the exception is resolved through.</param>
/// <param name="logger">Structured log sink for the full exception (Code + type).</param>
public sealed class ProviderExceptionHandler(
    ProblemHandlerRegistry registry,
    ILogger<ProviderExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var answer = registry.Resolve(exception);

        if (answer.ValidationErrors is { } validationErrors)
        {
            // A 400 is the caller's mistake, not a server fault — debug, not error.
            logger.LogDebug(
                "Request {RequestMethod} {RequestPath} failed validation ({ExceptionType})",
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.GetType().Name);

            await TypedResults.ValidationProblem(validationErrors).ExecuteAsync(httpContext);

            return true;
        }

        // Full exception with the assigned Code — log-only, never surfaced in the response body.
        logger.LogError(
            exception,
            "Request {RequestMethod} {RequestPath} mapped to {StatusCode} {ProblemCode} ({ExceptionType})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            answer.StatusCode,
            answer.Code,
            exception.GetType().Name);

        var problem = TypedResults.Problem(
            title: answer.Title,
            detail: answer.Detail,
            statusCode: answer.StatusCode,
            type: answer.Type,
            extensions: new Dictionary<string, object?>(answer.Extensions));

        await problem.ExecuteAsync(httpContext);

        return true;
    }
}
