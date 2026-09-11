using Comuki.Shared.Bootstrap.Correlation;

namespace Comuki.Host.Correlation;

/// <summary>
/// Establishes the ambient correlation id for every request, outermost in
/// the pipeline (issue #56 §5): a valid incoming <c>X-Request-Id</c> is
/// reused, anything else — missing, malformed, header-injection-shaped —
/// is replaced by a generated id. The id is installed into the
/// <see cref="ICorrelationIdAccessor"/> slot so the console formatters
/// stamp <c>rid=…</c> onto every log line of the request, and it is
/// echoed back on the response so operators can correlate both ways.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>The wire header carrying the correlation id both ways.</summary>
    public const string HeaderName = "X-Request-Id";

    /// <summary>Installs the correlation id for the rest of the request pipeline.</summary>
    /// <param name="context"></param>
    public async Task InvokeAsync(HttpContext context)
    {
        var accessor = context.RequestServices.GetRequiredService<ICorrelationIdAccessor>();
        var requestId = RequestCorrelation.Ensure(context.Request.Headers[HeaderName].ToString());

        context.Response.Headers[HeaderName] = requestId;
        using (accessor.Begin(requestId))
        {
            await next(context);
        }
    }
}

/// <summary>Validation and generation of the correlation id value.</summary>
file static class RequestCorrelation
{
    public const int MinimumLength = 8;

    public const int MaximumLength = 128;

    /// <summary>A valid candidate passes through; anything else gets a fresh id.</summary>
    public static string Ensure(string candidate)
    {
        return IsValid(candidate) ? candidate : Generate();
    }

    /// <summary>32 hex characters — the <c>N</c> Guid format.</summary>
    public static string Generate()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Length within the 8–128 window and only url-safe visible characters
    /// (letters, digits, dash, dot, underscore) — anything else is treated
    /// as hostile input and replaced rather than echoed into logs and
    /// responses.
    /// </summary>
    public static bool IsValid(string candidate)
    {
        if (candidate.Length is < MinimumLength or > MaximumLength)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            var isAllowed = character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '.' or '_';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }
}
