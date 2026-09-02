// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Linq.Expressions;
using Comuki.Shared.Filtering.Parser;
using Comuki.Shared.Filtering.Ports;

namespace Comuki.Shared.Filtering.Translator;

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
    /// <param name="clock">
    ///     Optional clock used to anchor <c>now(offset)</c> calls. When null,
    ///     <see cref="TimeProvider.System" /> is used. Production callers should
    ///     pass an injected clock so the translation is testable.
    /// </param>
    public static Expression<Func<TEntity, bool>>? ParseFor<TEntity>(
        string? source,
        FilterableFieldSet<TEntity>? fields = null,
        TimeProvider? clock = null)
    {
        var node = FilterParser.Parse(source);

        if (node is null)
        {
            return null;
        }

        var fieldSet = fields ?? FilterableFieldRegistry.For<TEntity>();
        var translator = new EfFilterTranslator<TEntity>(fieldSet, clock ?? TimeProvider.System);
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var now = translator.Now();
        var body = translator.Translate(node, parameter, now);

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }
}
