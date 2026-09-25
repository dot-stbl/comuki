namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// Marks a module type (a marker class, or an existing static installer
/// class if it can carry an attribute) as gated by the named feature.
/// Applied only at the DI-registration boundary by
/// <c>AddComukiModule&lt;TModule&gt;</c>; runtime endpoints and
/// controllers use <see cref="RequiresFeatureAttribute"/> instead.
/// </summary>
/// <remarks>
/// The string-typed key mirrors <see cref="RequiresFeatureAttribute"/>'s
/// OQ1 fallback decision (issue #164): C# attribute arguments must be
/// compile-time constants, and a <see cref="Catalog.Feature"/> instance
/// is built at runtime by <c>Feature.Define(...)</c>, so the attribute
/// cannot carry one. The call site writes the literal well-formed key,
/// e.g. <c>[EditionFeature("multi-repo")]</c> — NOT
/// <c>nameof(Features.MultiRepo)</c>, which yields the PascalCase C#
/// member name (<c>"MultiRepo"</c>), not the catalog's dash-case key
/// (<c>"multi-repo"</c>); the two never coincide today, and a nameof
/// call site fails <see cref="Catalog.Keys.FeatureKey.IsWellFormed"/>
/// at runtime.
/// </remarks>
/// <param name="featureKey">The well-formed feature key, e.g. <c>"multi-repo"</c>.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class EditionFeatureAttribute(string featureKey) : Attribute
{
    /// <summary>The well-formed feature key the module's services require.</summary>
    public string FeatureKey { get; } = featureKey;
}
