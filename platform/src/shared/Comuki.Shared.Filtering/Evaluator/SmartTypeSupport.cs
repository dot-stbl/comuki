// Ported from Hybrid.Sdk.Shared.Filtering (console.x.sdk) — fidelity over house style.
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Comuki.Shared.Filtering.Evaluator;

/// <summary>
///     Single source of truth for the engine's "smart-type" shape: a closed-set value
///     type that replaces a plain <c>enum</c> per <c>smart-types.md</c> — a
///     <c>readonly record struct</c> with a private constructor, a public
///     <c>static T FromWire(string)</c>, and a public <c>string Value</c>. Two callers
///     need to know the answer — <see cref="FilterOperatorRegistry" /> when inferring the
///     operator set for a property type, and <see cref="Translator.EfFilterTranslator{TEntity}" />
///     when converting a raw wire string to a CLR value at translation time — so the
///     detection and the conversion factory live here once, keyed by the same
///     <see cref="Type" />.
/// </summary>
/// <remarks>
///     The cache value encodes "is this a smart-type" by being non-null: a
///     <see cref="Func{T, TResult}" /> for a real smart-type, <c>null</c> for everything else
///     (primitives, real enums, strings, structs without a public
///     <c>FromWire(string)</c>). <see cref="GetFactory" /> and <see cref="IsSmartType" />
///     share that single entry — a non-null result is both proof of shape and the
///     converter, so there is no second reflection pass and no second cache to drift
///     out of sync with the first.
/// </remarks>
internal static class SmartTypeSupport
{
    /// <summary>
    ///     Per-type factory cache, value <see cref="Func{T, TResult}" /> for a real
    ///     smart-type and <c>null</c> for everything else. The null sentinel does
    ///     double duty: it is also the answer to <see cref="IsSmartType" />.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<string, object>?> factories = new();

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="type" /> is a smart-type — a closed-set
    ///     value type whose conversion path goes through its own public static
    ///     <c>FromWire(string)</c>. <see cref="string" />, primitive value types, and real
    ///     <c>enum</c>s are skipped up front so an unrelated struct with a coincidentally-named
    ///     <c>FromWire</c> method does not get treated as a smart-type.
    /// </summary>
    public static bool IsSmartType(Type type)
    {
        return GetFactory(type) is not null;
    }

    /// <summary>
    ///     Returns the cached <c>string → object</c> factory for <paramref name="type" />, or
    ///     <c>null</c> when the type is not a smart-type. Built once per
    ///     <see cref="Type" /> on first use; the per-request hot path pays no
    ///     reflection.
    /// </summary>
    public static Func<string, object>? GetFactory(Type type)
    {
        return factories.GetOrAdd(type, static t => BuildFactory(t));
    }

    private static Func<string, object>? BuildFactory(Type type)
    {
        if (!type.IsValueType || type.IsEnum || type.IsPrimitive)
        {
            return null;
        }

        var fromWire = type.GetMethod(
            "FromWire",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string)],
            modifiers: null);

        if (fromWire is null || fromWire.ReturnType != type)
        {
            return null;
        }

        // Delegate.CreateDelegate(typeof(Func<string, object>), fromWire) fails here:
        // every smart-type is a value type (readonly record struct), and CreateDelegate's
        // signature match does not box a value-type return into `object` the way a normal
        // method-group conversion does. Build the boxing conversion explicitly — compiled
        // once per type, cached for the process lifetime, so the per-request path pays no
        // reflection cost.
        var wireParameter = Expression.Parameter(typeof(string), "wire");

        return Expression.Lambda<Func<string, object>>(
            Expression.Convert(Expression.Call(fromWire, wireParameter), typeof(object)),
            wireParameter).Compile();
    }
}
