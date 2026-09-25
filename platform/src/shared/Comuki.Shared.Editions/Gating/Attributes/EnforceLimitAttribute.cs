namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// Declares that a controller action or minimal-API endpoint may be reached
/// only while the current <see cref="Edition.IEdition"/> reports the
/// current usage of the named limit (<see cref="Limits"/>) is below the
/// tier's cap. The string-typed key mirrors
/// <see cref="RequiresFeatureAttribute"/>: C# attribute arguments must
/// be compile-time constants and a <see cref="Catalog.Limit"/> instance
/// is built at runtime, so the attribute cannot carry one. The call site
/// writes <c>[EnforceLimit(nameof(Limits.Projects))]</c>.
/// </summary>
/// <remarks>
/// Last-wins ordering matches <see cref="RequiresFeatureAttribute"/>'s
/// convention: endpoint metadata is ordered least to most specific
/// (controller attributes before action attributes), and the filter /
/// middleware read the last entry, matching the framework's own reader.
/// </remarks>
/// <param name="limitKey">The well-formed limit key, e.g. <c>"projects"</c>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class EnforceLimitAttribute(string limitKey) : Attribute
{
    /// <summary>The well-formed limit key the demanding endpoint enforces.</summary>
    public string LimitKey { get; } = limitKey;
}
