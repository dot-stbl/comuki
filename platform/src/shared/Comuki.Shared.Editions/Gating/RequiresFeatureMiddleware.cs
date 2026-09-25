using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Registry;
using Microsoft.AspNetCore.Http;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// The action axis of the edition-gating model for non-MVC endpoints
/// (minimal APIs): reads <see cref="RequiresFeatureAttribute"/> and
/// <see cref="EnforceLimitAttribute"/> off the endpoint metadata
/// (handler-method attributes land there since net7) and runs them
/// through the shared <see cref="EditionGate"/>. MVC actions keep
/// <see cref="RequiresFeatureFilter"/> — both paths answer the same
/// 403 <c>problem+json</c> shape. Endpoints without a demand (health,
/// login, the worker runtime) pass straight through.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class RequiresFeatureMiddleware(RequestDelegate next)
{
    /// <inheritdoc />
    public async Task InvokeAsync(
        HttpContext context,
        IEdition edition,
        IEditionCapabilityRegistry registry,
        IEnumerable<ILimitUsageProvider> limitUsageProviders)
    {
        // Last wins, matching the MVC filter's metadata ordering.
        var featureDemand = context.GetEndpoint()?.Metadata
            .OfType<RequiresFeatureAttribute>()
            .LastOrDefault();

        var limitDemand = context.GetEndpoint()?.Metadata
            .OfType<EnforceLimitAttribute>()
            .LastOrDefault();

        if (featureDemand is null && limitDemand is null)
        {
            await next(context);
            return;
        }

        var cancellationToken = context.RequestAborted;

        // Feature demand evaluated first, matching the filter's order:
        // a missing feature is the louder failure mode and surfaces above
        // the softer limit-exhausted message when an endpoint carries
        // both attributes.
        if (featureDemand is not null
            && EditionGate.EvaluateFeature(registry, edition, featureDemand.FeatureKey) is { } featureDenial)
        {
            context.Response.StatusCode = featureDenial.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(featureDenial.Problem);
            return;
        }

        if (limitDemand is not null
            && await EditionGate.EvaluateLimitAsync(
                registry,
                edition,
                limitUsageProviders,
                limitDemand.LimitKey,
                cancellationToken) is { } limitDenial)
        {
            context.Response.StatusCode = limitDenial.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(limitDenial.Problem);
            return;
        }

        await next(context);
    }
}
