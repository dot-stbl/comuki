// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Query envelope accepted by every list endpoint that supports the filter DSL.
///     <see cref="Filter" /> + <see cref="Sort" /> are parsed by
///     <see cref="FilterParser" /> against the entity's
///     <see cref="FilterableFieldSet{TEntity}" />.
/// </summary>
/// <remarks>
///     <para><b>Filter grammar</b> (EBNF, AND binds tighter than OR):</para>
///     <code>
/// filter        := orExpr
/// orExpr        := andExpr ('|' andExpr)*
/// andExpr       := term (';' term)*
/// term          := '(' orExpr ')' | comparison
/// comparison    := field operator value
/// operator      := '==' | '!=' | '~' | '^=' | '$=' | '&gt;=' | '&lt;=' | '&gt;' | '&lt;'
///                |  '[]=' | '?' | '!?'
/// value         := '"' ... '"' | bare | function
/// function      := 'now' '(' signedDuration ')'
/// signedDuration := ['-']? digits unit
/// unit          := 's' | 'm' | 'h' | 'd' | 'w'
///     </code>
///     <para>
///         Null operators take no value: <c>field?</c> matches <c>null</c>,
///         <c>field!?</c> matches non-null. They are only available for
///         reference-type and <see cref="Nullable{T}" /> fields (the inference
///         table does not attach them to non-nullable value types, where the
///         predicate would always be false at runtime).
///     </para>
///     <para>
///         Function calls are evaluated server-side at request time and may only
///         be used on date/time fields. The <c>now(offset)</c> function returns
///         <c>DateTimeOffset.UtcNow + offset</c>; the offset is a signed integer
///         followed by a single-char unit (<c>s</c> seconds, <c>m</c> minutes,
///         <c>h</c> hours, <c>d</c> days, <c>w</c> weeks). The argument is
///         case-insensitive (<c>NOW(-7D)</c> works) but a leading <c>+</c> is
///         rejected to keep the grammar unambiguous (<c>+7d</c> is invalid; use
///         <c>7d</c>).
///     </para>
///     <para><b>Sort grammar</b>:</para>
///     <code>
/// sort     := criterion (';' criterion)*
/// criterion := field (',' direction)?
/// direction := 'asc' | 'desc'     (case-insensitive)
///     </code>
///     <para>
///         Multiple sort criteria apply as <c>OrderBy</c> + <c>ThenBy</c>: the
///         first criterion is the primary sort, each subsequent one breaks ties.
///         Unknown fields are silently skipped per criterion (stale-client safe).
///     </para>
///     <para>
///         <b>Examples:</b>
///         <list type="bullet">
///             <item>
///                 <c>name~John</c>
///             </item>
///             <item>
///                 <c>name~John;status==Active</c>
///             </item>
///             <item>
///                 <c>(name~John|name~Ivan);status==Active</c>
///             </item>
///             <item>
///                 <c>status[]=Active,Trial</c>
///             </item>
///             <item>
///                 <c>createdAt&gt;=2024-01-01;createdAt&lt;2025-01-01</c>
///             </item>
///             <item><c>createdAt&gt;=now(-7d)</c> — last 7 days (server-time)</item>
///             <item><c>updatedAt&gt;now(-1h);status==Active</c> — last hour, active</item>
///             <item><c>folderId?</c> — entities with no folder (unfiled bucket)</item>
///             <item><c>deletedAt!?</c> — entities that have not been deleted</item>
///             <item>
///                 <c>sort=name,asc;createdAt,desc</c>
///             </item>
///         </list>
///     </para>
///     <para>
///         Unknown fields, disallowed operators, values that cannot be converted
///         to the field's CLR type, and unknown function names or malformed function
///         arguments raise <see cref="FilterParseException" /> → <c>400 Bad Request</c>.
///     </para>
/// </remarks>
public sealed record FilterQuery
{
    /// <summary>Filter expression in DSL form. <c>null</c> or whitespace = no filter.</summary>
    [FromQuery(Name = "filter")]
    public string? Filter { get; init; }

    /// <summary>
    ///     Sort spec: <c>field</c> or <c>field,direction</c>. Direction is
    ///     <c>asc</c> or <c>desc</c> (case-insensitive). Unknown field falls back to
    ///     the entity's default sort.
    /// </summary>
    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    /// <summary>Page number, 1-based. Must be ≥ 1.</summary>
    [FromQuery(Name = "page")]
    public int Page { get; init; } = 1;

    /// <summary>Items per page. Clamped to [1, 100] by the handler.</summary>
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; init; } = 25;

    /// <summary>Normalizes pagination bounds. Filter/Sort pass through unchanged.</summary>
    public FilterQuery Normalized()
    {
        return this with
        {
            Page = Math.Max(1, Page),
            PageSize = Math.Clamp(PageSize, 1, 100)
        };
    }

    /// <summary>
    ///     Zero-based skip offset derived from <see cref="Page" /> + <see cref="PageSize" />.
    ///     Exposed as a method (not a property) so it stays out of model binding and the
    ///     OpenAPI query-parameter list — it is computed output, never a query input.
    /// </summary>
    public int Skip()
    {
        return Math.Max(0, (Page - 1) * PageSize);
    }
}
