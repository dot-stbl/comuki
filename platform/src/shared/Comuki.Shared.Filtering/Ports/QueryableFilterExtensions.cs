// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Linq.Expressions;

using Comuki.Shared.Filtering.Attributes;
using Comuki.Shared.Filtering.Parser;
using Comuki.Shared.Filtering.Translator;

namespace Comuki.Shared.Filtering.Ports;

/// <summary>
///     <see cref="IQueryable{T}" /> extensions that apply a parsed
///     <see cref="FilterQuery" /> against an entity type. Filter expression is parsed
///     via <see cref="FilterParser" />; sort is applied by name — any field in the registry is
///     sortable, and only those, so a property hidden with
///     <see cref="FilteredIgnoreAttribute" /> cannot be sorted by either.
/// </summary>
public static class QueryableFilterExtensions
{
    /// <summary>
    ///     Applies the <see cref="FilterQuery.Filter" /> clause to <paramref name="source" />.
    ///     Returns <paramref name="source" /> unchanged when the filter is null/empty.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="source"></param>
    /// <param name="filter"></param>
    /// <param name="fields"></param>
    public static IQueryable<TEntity> ApplyFilter<TEntity>(
        this IQueryable<TEntity> source,
        string? filter,
        FilterableFieldSet<TEntity>? fields = null,
        TimeProvider? clock = null)
    {
        var predicate = FilterExpression.ParseFor(filter, fields, clock);
        return predicate is null ? source : source.Where(predicate);
    }

    /// <summary>
    ///     Applies <see cref="FilterQuery.Sort" /> to <paramref name="source" />.
    ///     <para>Sort spec grammar (most-significant criterion first):</para>
    ///     <code>
    /// sort     := criterion (';' criterion)*
    /// criterion := field (',' direction)?
    /// direction := 'asc' | 'desc'     (case-insensitive)
    ///     </code>
    ///     <para>
    ///         Multiple criteria are applied as <c>OrderBy</c> + <c>ThenBy</c> — the
    ///         first criterion is the primary sort, each subsequent one breaks ties.
    ///         Unknown fields are silently skipped (stale-client safe); when no
    ///         criterion resolves, ordering falls back to the entity's first registered
    ///         field (stable, deterministic).
    ///     </para>
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="source"></param>
    /// <param name="sort"></param>
    /// <param name="fields"></param>
    public static IOrderedQueryable<TEntity> ApplySort<TEntity>(
        this IQueryable<TEntity> source,
        string? sort,
        FilterableFieldSet<TEntity>? fields = null)
    {
        var fieldSet = fields ?? FilterableFieldRegistry.For<TEntity>();

        var criteria = ParseSortCriteria(sort, fieldSet).ToList();

        if (criteria.Count > 0)
        {
            return BuildOrderedQueryable(source, criteria);
        }

        // Fall back: order by the first registered field. Stable, deterministic.
        var first = fieldSet.All.FirstOrDefault();
        return first is null
                ? (IOrderedQueryable<TEntity>)source
                : OrderBy(source, first.Accessor, false);
    }

    /// <summary>
    ///     Splits a sort spec into ordered (field, descending) criteria. Unknown
    ///     fields are skipped — a partially-known multi-field spec still applies the
    ///     criteria that resolve. Empty / whitespace / no-recognised-criterion yields
    ///     an empty sequence and triggers the default-sort fallback.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="sort"></param>
    /// <param name="fields"></param>
    private static IEnumerable<(FilterableField<TEntity> Field, bool Descending)> ParseSortCriteria<TEntity>(
        string? sort,
        FilterableFieldSet<TEntity> fields)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            yield break;
        }

        foreach (var criterion in sort.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = criterion.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                continue;
            }

            if (fields.Find(parts[0]) is not { } field)
            {
                continue;
            }

            var descending = parts.Length > 1
                             && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            yield return (field, descending);
        }
    }

    private static IOrderedQueryable<TEntity> BuildOrderedQueryable<TEntity>(
        IQueryable<TEntity> source,
        List<(FilterableField<TEntity> Field, bool Descending)> criteria)
    {
        var ordered = OrderBy(source, criteria[0].Field.Accessor, criteria[0].Descending);

        foreach (var (field, descending) in criteria.Skip(1))
        {
            ordered = ThenBy(ordered, field.Accessor, descending);
        }

        return ordered;
    }

    private static IOrderedQueryable<TEntity> OrderBy<TEntity>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, object?>> accessor,
        bool descending)
    {
        var (call, _) = BuildOrderCallExpression(
            source.Expression,
            accessor,
            descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy));

        return (IOrderedQueryable<TEntity>)source.Provider.CreateQuery<TEntity>(call);
    }

    private static IOrderedQueryable<TEntity> ThenBy<TEntity>(
        IOrderedQueryable<TEntity> source,
        Expression<Func<TEntity, object?>> accessor,
        bool descending)
    {
        var (call, _) = BuildOrderCallExpression(
            source.Expression,
            accessor,
            descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy));

        return (IOrderedQueryable<TEntity>)source.Provider.CreateQuery<TEntity>(call);
    }

    /// <summary>
    ///     Builds the static <see cref="Queryable" /> call for either
    ///     <c>OrderBy</c>/<c>OrderByDescending</c> or <c>ThenBy</c>/<c>ThenByDescending</c>.
    ///     Unwraps the accessor's <c>Convert(x.Property, object)</c> boxing so the
    ///     <c>Queryable</c> method receives the property's real CLR type — required for
    ///     EF Core to translate the expression to SQL with the correct column type.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="sourceExpression"></param>
    /// <param name="accessor"></param>
    /// <param name="methodName"></param>
    private static (MethodCallExpression Call, Expression Body) BuildOrderCallExpression<TEntity>(
        Expression sourceExpression,
        Expression<Func<TEntity, object?>> accessor,
        string methodName)
    {
        var body = accessor.Body is UnaryExpression { NodeType: ExpressionType.Convert } unary
                ? unary.Operand
                : accessor.Body;

        var typed = Expression.Lambda(body, accessor.Parameters[0]);
        var call = Expression.Call(
            typeof(Queryable),
            methodName,
            [typeof(TEntity), body.Type],
            sourceExpression,
            typed);

        return (call, body);
    }
}
