using Comuki.Shared.Editions.Catalog;
using Microsoft.AspNetCore.Builder;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// Minimal-API fluent demand: <c>group.MapPost(...).RequireFeature(Features.X)</c>
/// or <c>.EnforceLimit(Limits.Projects)</c> on the
/// <see cref="IEndpointConventionBuilder"/> the routing layer hands back.
/// Both paths add the same attribute-shaped metadata to the endpoint
/// (issue #164 §3a task 4.3 — "both paths add the same attribute-shaped
/// metadata to the endpoint"), so a request that lands through the
/// minimal-API middleware reads exactly the same
/// <see cref="RequiresFeatureAttribute"/> / <see cref="EnforceLimitAttribute"/>
/// it would have read through the MVC resource filter, and the
/// attribute-on-method form keeps working for cases where the
/// convention-builder form would be awkward (e.g. a base controller
/// class that wants every derived action gated).
/// </summary>
public static class EditionGateConventionBuilderExtensions
{
    /// <summary>Adds a <see cref="RequiresFeatureAttribute"/> for <paramref name="feature"/> to the endpoint's metadata.</summary>
    /// <typeparam name="TBuilder">The convention-builder type the routing layer returns.</typeparam>
    /// <param name="builder">The minimal-API convention builder.</param>
    /// <param name="feature">The catalog feature to demand.</param>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, Feature feature)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(new RequiresFeatureAttribute(feature.Key.Value)));
        return builder;
    }

    /// <summary>Adds an <see cref="EnforceLimitAttribute"/> for <paramref name="limit"/> to the endpoint's metadata.</summary>
    /// <typeparam name="TBuilder">The convention-builder type the routing layer returns.</typeparam>
    /// <param name="builder">The minimal-API convention builder.</param>
    /// <param name="limit">The catalog limit to enforce.</param>
    public static TBuilder EnforceLimit<TBuilder>(this TBuilder builder, Limit limit)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(new EnforceLimitAttribute(limit.Key.Value)));
        return builder;
    }
}
