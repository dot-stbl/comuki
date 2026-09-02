// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Collections;
using System.Globalization;
using System.Linq.Expressions;

namespace Comuki.Shared.Filtering;

/// <summary>
///     Walks a neutral <see cref="FilterNode" /> tree and builds a LINQ
///     <see cref="Expression" /> for EF Core (PostgreSQL). Resolves field names against
///     <see cref="FilterableFieldSet{TEntity}" />, converts raw string values to the field's
///     CLR type, and delegates expression construction to
///     <see cref="FilterOperatorDescriptor.BuildExpression" />.
/// </summary>
/// <typeparam name="TEntity">The entity type to filter.</typeparam>
/// <param name="fieldSet"></param>
internal sealed class EfFilterTranslator<TEntity>(FilterableFieldSet<TEntity> fieldSet)
{
    /// <summary>
    ///     Translates a <see cref="FilterNode" /> tree into an EF <see cref="Expression" />
    ///     over the given <paramref name="parameter" />.
    /// </summary>
    /// <param name="node"></param>
    /// <param name="parameter"></param>
    /// <exception cref="NotSupportedException"></exception>
    public Expression Translate(FilterNode node, ParameterExpression parameter)
    {
        return node switch
        {
            AndNode and => TranslateAnd(and, parameter),
            OrNode or => TranslateOr(or, parameter),
            ComparisonNode cmp => TranslateComparison(cmp, parameter),
            _ => throw new NotSupportedException($"Unknown FilterNode type: {node.GetType().Name}")
        };
    }

    private Expression TranslateAnd(AndNode and, ParameterExpression parameter)
    {
        if (and.Children.Length == 0)
        {
            throw new InvalidOperationException("AndNode has no children.");
        }

        Expression? accumulated = null;

        foreach (var child in and.Children)
        {
            accumulated = accumulated is null
                    ? Translate(child, parameter)
                    : Expression.AndAlso(accumulated, Translate(child, parameter));
        }

        return accumulated!;
    }

    private Expression TranslateOr(OrNode or, ParameterExpression parameter)
    {
        if (or.Children.Length == 0)
        {
            throw new InvalidOperationException("OrNode has no children.");
        }

        Expression? accumulated = null;

        foreach (var child in or.Children)
        {
            accumulated = accumulated is null
                    ? Translate(child, parameter)
                    : Expression.OrElse(accumulated, Translate(child, parameter));
        }

        return accumulated!;
    }

    private Expression TranslateComparison(ComparisonNode cmp, ParameterExpression parameter)
    {
        var field = fieldSet.Find(cmp.Field)
                    ?? throw new FilterParseException($"Unknown field '{cmp.Field}'");

        if ((field.Operators & cmp.Operator) == FilterOperator.None)
        {
            throw new FilterParseException(
                $"Field '{field.Name}' does not support operator '{cmp.Operator}'");
        }

        var accessor = Expression.Property(parameter, field.Name);
        var descriptor = FilterOperatorRegistry.Get(cmp.Operator);

        var convertedValue = ConvertValue(cmp, field, descriptor.ValueKind);

        return descriptor.BuildExpression(accessor, convertedValue, field.ValueType);
    }

    /// <summary>
    ///     Converts the raw string value(s) from the neutral node to the field's CLR type.
    ///     Handles function calls (now(-7d)), scalar values, and IN-lists.
    /// </summary>
    /// <param name="cmp"></param>
    /// <param name="field"></param>
    /// <param name="valueKind"></param>
    /// <exception cref="NotSupportedException"></exception>
    private static object? ConvertValue(ComparisonNode cmp, FilterableField<TEntity> field, ValueKind valueKind)
    {
        return valueKind switch
        {
            ValueKind.None => null,
            ValueKind.Scalar => ConvertScalar(cmp, field),
            ValueKind.List => ConvertList(cmp.Value, field),
            _ => throw new NotSupportedException($"Unknown ValueKind: {valueKind}")
        };
    }

    private static object? ConvertScalar(ComparisonNode cmp, FilterableField<TEntity> field)
    {
        var rawValue = cmp.Value;

        // Function call (now(-7d)) — only when the parser tagged the value as one.
        // The old heuristic (EndsWith(')') && Contains('(')) misfired on quoted
        // strings like "John; Doe (admin)" because they contain both.
        return cmp.ValueKind == FilterValueKind.FunctionCall && rawValue is string fnCall
            ? EvaluateFunction(fnCall, field)
            : rawValue is string s ? ConvertValue(s, field.ValueType) : rawValue;
    }

    private static IList ConvertList(object? rawValue, FilterableField<TEntity> field)
    {
        if (rawValue is not List<string> strings)
        {
            throw new FilterParseException("Expected a value list for IN/NotIn operator.");
        }

        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(field.ValueType))!;

        foreach (var s in strings)
        {
            list.Add(ConvertValue(s, field.ValueType));
        }

        return list;
    }

    private static DateTimeOffset EvaluateFunction(string call, FilterableField<TEntity> field)
    {
        var valueType = field.ValueType;

        if (valueType != typeof(DateTimeOffset) && valueType != typeof(DateTime))
        {
            throw new FilterParseException(
                $"Function '{call}' returns a timestamp and can only be used on date/time fields.");
        }

        var parts = call.TrimEnd(')').Split('(', 2);
        var functionName = parts[0];
        var argument = parts.Length > 1 ? parts[1] : string.Empty;

        return FilterFunctions.EvaluateNow(functionName, argument, 0);
    }

    private static object ConvertValue(string text, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            return underlying switch
            {
                _ when underlying == typeof(string) => text,
                _ when underlying.IsEnum => Enum.Parse(underlying, text, true),
                _ when underlying == typeof(DateTimeOffset) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                _ when underlying == typeof(DateTime) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                _ when underlying == typeof(Guid) => Guid.Parse(text),
                _ when underlying == typeof(TimeSpan) => TimeSpan.Parse(text, CultureInfo.InvariantCulture),
                _ => Convert.ChangeType(text, underlying, CultureInfo.InvariantCulture)
            };
        }
        catch (Exception ex)
        {
            throw new FilterParseException($"Cannot convert '{text}' to {underlying.Name}", 0, ex);
        }
    }
}
