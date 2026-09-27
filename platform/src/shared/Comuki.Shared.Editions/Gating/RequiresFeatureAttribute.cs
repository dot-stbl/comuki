namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// Declares that a controller action or minimal-API endpoint is reachable
/// only when the current <see cref="Edition.IEdition"/> covers the named
/// feature (<see cref="Features"/>). The string-typed key is a
/// deliberate compromise (issue #164 / OQ1 (a)): C# attribute arguments
/// must be compile-time constants, and a <see cref="Catalog.Feature"/>
/// instance is built at runtime by <c>Feature.Define(...)</c>, so the
/// attribute cannot carry one. The call site writes the literal
/// well-formed key, e.g. <c>[RequiresFeature("multi-repo")]</c> — NOT
/// <c>nameof(Features.MultiRepo)</c>, which yields the PascalCase C#
/// member name (<c>"MultiRepo"</c>), not the catalog's dash-case key
/// (<c>"multi-repo"</c>); the two never coincide today, and a nameof
/// call site fails <see cref="Catalog.Keys.FeatureKey.IsWellFormed"/> at
/// runtime. A future source-generator step is the documented escape
/// hatch when hand-writing the literal key becomes unbearable.
/// </summary>
/// <remarks>
/// <para>
/// Last-wins ordering matches <c>RequiresPermissionAttribute</c>'s
/// convention: endpoint metadata is ordered least to most specific
/// (controller attributes before action attributes), and the filter /
/// middleware read the last entry, matching the framework's own reader.
/// </para>
/// <para>
/// Mirrors the Identity module's <c>RequiresPermissionAttribute</c>
/// shape exactly (issue #164 §3a).
/// </para>
/// </remarks>
/// <param name="featureKey">The well-formed feature key, e.g. <c>"multi-repo"</c>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresFeatureAttribute(string featureKey) : Attribute
{
    /// <summary>The well-formed feature key the demanding endpoint requires.</summary>
    public string FeatureKey { get; } = featureKey;
}
