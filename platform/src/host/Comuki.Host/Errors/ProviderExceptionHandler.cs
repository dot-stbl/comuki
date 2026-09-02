using Comuki.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Comuki.Host.Errors;

/// <summary>
/// The single composition-root <see cref="IExceptionHandler"/>: typed
/// exceptions → <c>application/problem+json</c> per
/// <c>error-mapping.md</c> §4. Endpoints stay clean of error plumbing;
/// the one place that decides what a thrown exception means on the wire.
/// Per-module <c>*Problems</c> helpers continue to carry module-specific
/// 4xx codes (auth, permission, validation) until PR #20 replaces them.
/// </summary>
/// <param name="logger">Structured log sink for the full exception (Code + type).</param>
public sealed class ProviderExceptionHandler(ILogger<ProviderExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var mapped = ExceptionMapping.Map(exception);

        // Full exception with the assigned Code — log-only, never surfaced in the response body.
        logger.LogError(
            exception,
            "Request {RequestMethod} {RequestPath} mapped to {StatusCode} {ProblemCode} ({ExceptionType})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            mapped.StatusCode,
            mapped.Code,
            exception.GetType().Name);

        var problem = TypedResults.Problem(
            title: mapped.Title,
            detail: mapped.Detail,
            statusCode: mapped.StatusCode,
            type: mapped.Type,
            extensions: new Dictionary<string, object?> { ["code"] = mapped.Code });

        await problem.ExecuteAsync(httpContext);

        return true;
    }
}

/// <summary>
/// Typed-exception → ProblemDetails mapping table. One pass per request:
/// the inheritance chain is walked at most once via a single
/// pattern-match arm; the most-derived matching type wins. Pure function
/// — no I/O, no DI.
/// </summary>
file static class ExceptionMapping
{
    /// <summary>One wire row: status + RFC 9457 fields + the machine <c>code</c>.</summary>
    /// <param name="StatusCode">HTTP status the response carries.</param>
    /// <param name="Type">Stable URI for the error class (per RFC 9457).</param>
    /// <param name="Title">Short, stable, human.</param>
    /// <param name="Detail">Safe human detail — no stack, no secret, no PII.</param>
    /// <param name="Code">Stable dot.case identifier — clients branch on this.</param>
    public sealed record ProblemRow(int StatusCode, string Type, string Title, string Detail, string Code);

    /// <summary>
    /// Map any <see cref="Exception"/> to its ProblemDetails row. Pattern
    /// matching evaluates the most-derived type first, so subclasses of
    /// <see cref="ProviderException"/> (Timeout, NotFound) win over the
    /// base, and any subclass of <see cref="DomainException"/> falls into
    /// the 422 arm without listing each one.
    /// </summary>
    /// <param name="exception">The thrown exception.</param>
    public static ProblemRow Map(Exception exception)
    {
        return exception switch
        {
            ProviderTimeoutException provider => new ProblemRow(
                StatusCodes.Status504GatewayTimeout,
                TypeUri("provider.timeout"),
                "Upstream timeout",
                "the upstream service timed out",
                provider.Code),
            ProviderNotFoundException provider => new ProblemRow(
                StatusCodes.Status404NotFound,
                TypeUri(provider.Code),
                "Resource not found",
                provider.Message,
                provider.Code),
            ProviderException provider => new ProblemRow(
                StatusCodes.Status502BadGateway,
                TypeUri(provider.Code),
                "Upstream unavailable",
                "the upstream service is unavailable",
                provider.Code),
            DomainException domain => new ProblemRow(
                StatusCodes.Status422UnprocessableEntity,
                TypeUri(domain.Code),
                "Domain rule violated",
                domain.Message,
                domain.Code),
            _ => new ProblemRow(
                StatusCodes.Status500InternalServerError,
                "about:blank",
                "Internal server error",
                "an unexpected error occurred",
                "internal.error"),
        };
    }

    /// <summary>
    /// Builds a stable RFC 9457 <c>type</c> URI from the dot.case code.
    /// Stable across releases so clients can pin to a known identifier
    /// without parsing the human-readable title.
    /// </summary>
    /// <param name="code">Stable dot.case identifier.</param>
    private static string TypeUri(string code)
    {
        return $"urn:comuki:error:{code}";
    }
}
