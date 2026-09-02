// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Linq.Expressions;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Entry point for parsing filter DSL strings into LINQ predicates for EF Core.
///     Delegates to <see cref="FilterParser" /> (neutral, TEntity-free) + walks the
///     resulting <see cref="FilterNode" /> tree via the EF translator.
/// </summary>
public static class FilterExpression
{
    /// <summary>
    ///     Hard cap on nested parenthesis depth. Bounds stack usage against
    ///     adversarial inputs that emit <c>((((...))))</c> × 10K (audit finding 1e).
    /// </summary>
    public const int MaxParenDepth = 32;

    /// <summary>
    ///     Parses <paramref name="source" /> and returns the predicate, or <c>null</c>
    ///     when <paramref name="source" /> is null/empty/whitespace.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to filter.</typeparam>
    /// <param name="source">The DSL string.</param>
    /// <param name="fields">Optional pre-built field set; defaults to registry lookup.</param>
    public static Expression<Func<TEntity, bool>>? ParseFor<TEntity>(
        string? source,
        FilterableFieldSet<TEntity>? fields = null)
    {
        var node = FilterParser.Parse(source);

        if (node is null)
        {
            return null;
        }

        var fieldSet = fields ?? FilterableFieldRegistry.For<TEntity>();
        var translator = new EfFilterTranslator<TEntity>(fieldSet);
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var body = translator.Translate(node, parameter);

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }
}
