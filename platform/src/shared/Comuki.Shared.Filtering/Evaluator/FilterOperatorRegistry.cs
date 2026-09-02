// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Collections;
using System.Linq.Expressions;

using Comuki.Shared.Filtering.Ast;
using Comuki.Shared.Filtering.Parser;

namespace Comuki.Shared.Filtering.Evaluator;

/// <summary>
///     Single source of truth for every filter operator. Adding a new operator is
///     one entry here — the lexer, the type-inference table, the parser, and the
///     expression builder all read from this registry, so no subsystem can drift.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this shape.</b> Before this registry existed, each operator was
///         described in five separate places (the enum, a type→operator inference
///         switch, a lexer symbol-if-chain, a parser value-reading switch, and an
///         expression-builder switch). Forgetting one arm produced a runtime
///         <see cref="NotSupportedException" /> instead of a compile error. The
///         registry collapses all five into one declarative table: a missing
///         descriptor is an explicit lookup failure, never a silent fall-through.
///     </para>
///     <para>
///         <b>Lexer symbol matching.</b> Symbols are matched longest-prefix-first
///         (see <see cref="SymbolsByDescendingLength" />), so <c>~*</c> wins over
///         <c>~</c> and <c>![]=</c> wins over <c>!=</c>. Keep new multi-char symbols
///         a strict prefix-extension of any existing one, or the ordering breaks.
///     </para>
/// </remarks>
public static class FilterOperatorRegistry
{
    private static readonly Dictionary<FilterOperator, FilterOperatorDescriptor> descriptors =
            BuildDescriptors();

    /// <summary>All operator descriptors, keyed by <see cref="FilterOperator" />.</summary>
    public static IReadOnlyDictionary<FilterOperator, FilterOperatorDescriptor> All => descriptors;

    /// <summary>
    ///     Symbols sorted by descending length, for the lexer's longest-prefix
    ///     matcher. Cache once — the lexer is a hot path.
    /// </summary>
    public static IReadOnlyList<(string Symbol, FilterOperator Operator)> SymbolsByDescendingLength { get; } =
    [
        .. descriptors.Values
                .OrderByDescending(static d => d.Symbol.Length)
                .Select(static d => (d.Symbol, d.Operator))
    ];

    /// <summary>
    ///     Returns the descriptor for <paramref name="operator" />, or throws an
    ///     explicit error when the operator is unregistered (<c>None</c> or a
    ///     future value nobody added a descriptor for).
    /// </summary>
    /// <param name="operator"></param>
    /// <exception cref="FilterParseException"></exception>
    public static FilterOperatorDescriptor Get(FilterOperator @operator)
    {
        return descriptors.TryGetValue(@operator, out var descriptor)
                ? descriptor
                : throw new FilterParseException($"Operator '{@operator}' is not registered in {nameof(FilterOperatorRegistry)}");
    }

    /// <summary>The operators a CLR property type supports, derived from <see cref="FilterOperatorDescriptor.SupportsType" />.</summary>
    /// <param name="type"></param>
    public static FilterOperator OperatorsFor(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        var baseOps = FilterOperator.None;

        foreach (var descriptor in descriptors.Values)
        {
            if (descriptor.Operator is FilterOperator.IsNull or FilterOperator.IsNotNull)
            {
                continue; // null-ops attached separately below
            }

            if (descriptor.SupportsType(underlying))
            {
                baseOps |= descriptor.Operator;
            }
        }

        // Null ops attach only to fields whose type permits null at runtime
        // (reference types + Nullable<T>), matching the old FilterOperatorInference.
        var supportsNull = baseOps != FilterOperator.None
                           && (Nullable.GetUnderlyingType(type) is not null || type.IsClass);

        return supportsNull ? baseOps | FilterOperator.IsNull | FilterOperator.IsNotNull : baseOps;
    }

    private static Dictionary<FilterOperator, FilterOperatorDescriptor> BuildDescriptors()
    {
        var list = new[]
        {
            Scalar(FilterOperator.Eq,
                "==",
                IsFilterable,
                static (field, value, _) => Expression.Equal(field, Constant(value, field.Type))),
            Scalar(FilterOperator.NotEq,
                "!=",
                static t => IsFilterable(t) && IsNotBoolean(t),
                static (field, value, _) => Expression.NotEqual(field, Constant(value, field.Type))),
            Scalar(FilterOperator.Gt,
                ">",
                IsOrdered,
                static (field, value, _) => Expression.GreaterThan(field, Constant(value, field.Type))),
            Scalar(FilterOperator.Gte,
                ">=",
                IsOrdered,
                static (field, value, _) => Expression.GreaterThanOrEqual(field, Constant(value, field.Type))),
            Scalar(FilterOperator.Lt,
                "<",
                IsOrdered,
                static (field, value, _) => Expression.LessThan(field, Constant(value, field.Type))),
            Scalar(FilterOperator.Lte,
                "<=",
                IsOrdered,
                static (field, value, _) => Expression.LessThanOrEqual(field, Constant(value, field.Type))),

            // Case-sensitive string ops.
            Scalar(FilterOperator.Contains,
                "~",
                IsString,
                static (field, value, _) => StringCall(field, value, nameof(string.Contains))),
            Scalar(FilterOperator.StartsWith,
                "^=",
                IsString,
                static (field, value, _) => StringCall(field, value, nameof(string.StartsWith))),
            Scalar(FilterOperator.EndsWith,
                "$=",
                IsString,
                static (field, value, _) => StringCall(field, value, nameof(string.EndsWith))),

            // Case-insensitive string ops (PG-style symbols).
            Scalar(FilterOperator.IContains,
                "~*",
                IsString,
                static (field, value, _) => IStringCall(field, value, nameof(string.Contains))),
            Scalar(FilterOperator.IStartsWith,
                "^=*",
                IsString,
                static (field, value, _) => IStringCall(field, value, nameof(string.StartsWith))),
            Scalar(FilterOperator.IEndsWith,
                "$=*",
                IsString,
                static (field, value, _) => IStringCall(field, value, nameof(string.EndsWith))),

            // Membership / non-membership.
            List(FilterOperator.In,
                "[]=",
                IsEnumerable,
                static (field, value, valueType) => BuildIn(valueType, field, value as IList)),
            List(FilterOperator.NotIn,
                "![]=",
                IsEnumerable,
                static (field, value, valueType) => Expression.Not(BuildIn(valueType, field, value as IList))),

            // Null-check predicates — no value, direct null comparison.
            Nullary(FilterOperator.IsNull,
                "?",
                static (field, _, _) => Expression.Equal(field, Expression.Constant(null, field.Type))),
            Nullary(FilterOperator.IsNotNull,
                "!?",
                static (field, _, _) => Expression.NotEqual(field, Expression.Constant(null, field.Type)))
        };

        var dict = new Dictionary<FilterOperator, FilterOperatorDescriptor>(list.Length);

        foreach (var descriptor in list)
        {
            if (!dict.TryAdd(descriptor.Operator, descriptor))
            {
                throw new InvalidOperationException($"Duplicate descriptor for {descriptor.Operator}");
            }
        }

        return dict;
    }

    private static FilterOperatorDescriptor Scalar(
        FilterOperator op,
        string symbol,
        Func<Type, bool> supports,
        Func<Expression, object?, Type, Expression> build)
    {
        return new FilterOperatorDescriptor(op, symbol, ValueKind.Scalar, supports, build);
    }

    private static FilterOperatorDescriptor List(
        FilterOperator op,
        string symbol,
        Func<Type, bool> supports,
        Func<Expression, object?, Type, Expression> build)
    {
        return new FilterOperatorDescriptor(op, symbol, ValueKind.List, supports, build);
    }

    private static FilterOperatorDescriptor Nullary(
        FilterOperator op,
        string symbol,
        Func<Expression, object?, Type, Expression> build)
    {
        return new FilterOperatorDescriptor(op, symbol, ValueKind.None, static _ => false, build);
    }

    private static bool IsString(Type type)
    {
        return Type.GetTypeCode(type) == TypeCode.String;
    }

    /// <summary>
    ///     A type is filterable at all when it matches one of the category checks
    ///     (string / enumerable / ordered / boolean). Unknown types (byte[],
    ///     object, custom classes without a known mapping) return false here, so
    ///     Eq/NotEq don't silently attach to them — the field is excluded instead.
    /// </summary>
    /// <param name="type"></param>
    private static bool IsFilterable(Type type)
    {
        return IsString(type) || IsEnumerable(type) || IsOrdered(type) || Type.GetTypeCode(type) == TypeCode.Boolean;
    }

    /// <summary>
    ///     Boolean gets only Eq — NotEq on a bool is semantically odd (IsActive != true)
    ///     and the old inference table excluded it. Keep that rule to avoid widening
    ///     the operator surface for bool fields.
    /// </summary>
    /// <param name="type"></param>
    private static bool IsNotBoolean(Type type)
    {
        return Type.GetTypeCode(type) != TypeCode.Boolean;
    }

    private static bool IsEnumerable(Type type)
    {
        return type.IsEnum
               || type == typeof(Guid)
               || IsNumericTypeCode(Type.GetTypeCode(type));
    }

    private static bool IsOrdered(Type type)
    {
        return !type.IsEnum // enums report underlying numeric TypeCode — exclude them so they only get enumerable ops (In/NotIn), not range
               && (type == typeof(DateTimeOffset)
                   || type == typeof(DateTime)
                   || type == typeof(DateOnly)
                   || type == typeof(TimeOnly)
                   || type == typeof(TimeSpan)
                   || IsNumericTypeCode(Type.GetTypeCode(type)));
    }

    private static bool IsNumericTypeCode(TypeCode code)
    {
        return code is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
                or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
    }

    private static ConstantExpression Constant(object? value, Type type)
    {
        return value is DateTimeOffset timestamp && type == typeof(DateTime)
                ? Expression.Constant(timestamp.UtcDateTime, typeof(DateTime))
                : Expression.Constant(value, type);
    }

    private static MethodCallExpression StringCall(Expression field, object? value, string methodName)
    {
        var method = typeof(string).GetMethod(methodName, [typeof(string)])!;
        return Expression.Call(field, method, Constant(value, typeof(string)));
    }

    private static MethodCallExpression IStringCall(Expression field, object? value, string methodName)
    {
        var toLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        var method = typeof(string).GetMethod(methodName, [typeof(string)])!;
        var loweredField = Expression.Call(field, toLower);
        var loweredValue = Expression.Call(Constant(value, typeof(string)), toLower);
        return Expression.Call(loweredField, method, loweredValue);
    }

    private static Expression BuildIn(Type valueType, Expression field, IList? values)
    {
        if (values is null || values.Count == 0)
        {
            throw new FilterParseException("IN/NOT IN operator requires at least one value");
        }

        Expression? accumulated = null;

        foreach (var item in values)
        {
            var actualType = item?.GetType() ?? valueType;
            var constant = Expression.Constant(item, actualType);
            Expression normalized = actualType == valueType
                    ? constant
                    : Expression.Convert(constant, valueType);

            var comparison = Expression.Equal(field, normalized);
            accumulated = accumulated is null ? comparison : Expression.OrElse(accumulated, comparison);
        }

        return accumulated!;
    }
}
