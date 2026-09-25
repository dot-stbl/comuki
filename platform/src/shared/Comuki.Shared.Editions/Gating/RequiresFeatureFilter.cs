using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Registry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// The action axis of the edition-gating model, enforced once per request
/// for every MVC action: reads <see cref="RequiresFeatureAttribute"/> and
/// <see cref="EnforceLimitAttribute"/> off the endpoint metadata, runs
/// them through <see cref="EditionGate"/>, and short-circuits with a 403
/// <c>problem+json</c> body on deny. Minimal-API endpoints are covered
/// by <see cref="RequiresFeatureMiddleware"/>; both share
/// <see cref="EditionGate"/> so the decision exists once.
/// </summary>
/// <param name="edition">The runtime read-side of the current license.</param>
/// <param name="registry">The in-memory catalog every gate call site reads from.</param>
/// <param name="limitUsageProviders">Concrete limit-usage providers, looked up by <see cref="ILimitUsageProvider.LimitKey"/>.</param>
/// <remarks>
/// A resource filter rather than an authorization filter deliberately:
/// this filter owns the whole check inline, and the resource stage wraps
/// model binding and result execution, so the decision cannot be
/// bypassed by an earlier short-circuit. Mirrors the Identity module's
/// <c>RequiresPermissionFilter</c> shape (issue #164 §3a), which is the
/// canonical precedent for "deny directly with <c>problem+json</c>"
/// — see the openspec change
/// <c>add-editions-and-licensing/specs/host/spec.md</c>'s
/// <c>GET /api/v1/edition</c>-relevant edition-feature / edition-limit
/// mapping requirement for the wire-shape rationale.
/// </remarks>
public sealed class RequiresFeatureFilter(
    IEdition edition,
    IEditionCapabilityRegistry registry,
    IEnumerable<ILimitUsageProvider> limitUsageProviders) : IAsyncResourceFilter
{
    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // Last wins — endpoint metadata is ordered least to most specific
        // (controller attributes before action attributes), matching the
        // framework's own reader.
        var featureDemand = context.ActionDescriptor.EndpointMetadata
            .OfType<RequiresFeatureAttribute>()
            .LastOrDefault();

        var limitDemand = context.ActionDescriptor.EndpointMetadata
            .OfType<EnforceLimitAttribute>()
            .LastOrDefault();

        if (featureDemand is null && limitDemand is null)
        {
            await next();
            return;
        }

        var cancellationToken = context.HttpContext.RequestAborted;

        // Feature demand evaluated first: a missing feature is the louder
        // failure mode and surfaces above the softer limit-exhausted
        // message when an endpoint carries both attributes. The HTTP
        // method is passed through so EditionGate can apply the
        // read-only-degrade branch (GET/HEAD pass past grace, every
        // other method is refused — see EditionGate.EvaluateFeature).
        if (featureDemand is not null
            && EditionGate.EvaluateFeature(registry, edition, featureDemand.FeatureKey, context.HttpContext.Request.Method) is { } featureDenial)
        {
            context.Result = new ObjectResult(featureDenial.Problem)
            {
                StatusCode = featureDenial.StatusCode,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        // Limit branch is advisory — the authoritative transactional
        // check lives in the handler. A fast-fail here stops the request
        // from burning a DB transaction, but the handler is the source
        // of truth under concurrent writers.
        if (limitDemand is not null
            && await EditionGate.EvaluateLimitAsync(registry, edition, limitUsageProviders, limitDemand.LimitKey, cancellationToken) is { } limitDenial)
        {
            context.Result = new ObjectResult(limitDenial.Problem)
            {
                StatusCode = limitDenial.StatusCode,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        await next();
    }
}
