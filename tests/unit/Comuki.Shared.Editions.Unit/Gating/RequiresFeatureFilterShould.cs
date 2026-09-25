using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Registry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Gating;

/// <summary>
/// RequiresFeatureFilter tests — cover the shared <c>EditionGate</c>
/// decision function (called by both the filter and the middleware) and
/// the MVC resource filter that surfaces the decision as a 403
/// <c>problem+json</c>. The middleware path's coverage lives in
/// <see cref="RequiresFeatureMiddlewareShould"/>; the gate's coverage
/// lives here because the gate's two methods are the one decision both
/// paths share, and putting it next to the filter's metadata-walking
/// tests keeps the static-method + instance-method pair in one file.
/// </summary>
public sealed class RequiresFeatureFilterShould
{
    [Fact(DisplayName = "Given a community edition without the feature, when EvaluateFeature runs, then it returns a 403 denial with feature_unavailable code and the feature extension")]
    public void EvaluateFeatureDeniesCommunityEdition()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Has(Arg.Any<Feature>()).Returns(false);

        var denial = EditionGate.EvaluateFeature(registry, edition, "multi-repo");

        denial.ShouldNotBeNull();
        denial.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        denial.Problem.Status.ShouldBe(StatusCodes.Status403Forbidden);
        denial.Problem.Extensions.ShouldNotBeNull();
        denial.Problem.Extensions!["code"].ShouldBe(RequiresFeatureFilter.FeatureUnavailableCode);
        denial.Problem.Extensions["feature"].ShouldBe("multi-repo");
        denial.Problem.Extensions.ShouldContainKey("minimumTier");
        denial.Problem.Extensions["minimumTier"].ShouldBe("team");
    }

    [Fact(DisplayName = "Given a paid edition that covers the feature, when EvaluateFeature runs, then it returns null (no denial)")]
    public void EvaluateFeatureAllowsPaidEdition()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Has(Arg.Any<Feature>()).Returns(true);

        var denial = EditionGate.EvaluateFeature(registry, edition, "multi-repo");

        denial.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a malformed key, when EvaluateFeature runs, then it returns a 403 denial (fail closed)")]
    public void EvaluateFeatureDeniesMalformedKey()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);

        var denial = EditionGate.EvaluateFeature(registry, edition, "BAD_KEY");

        denial.ShouldNotBeNull();
        denial.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        denial.Problem.Extensions!["code"].ShouldBe(RequiresFeatureFilter.FeatureUnavailableCode);
        denial.Problem.Extensions["feature"].ShouldBe("BAD_KEY");
        // No minimumTier extension when the key is malformed: we cannot
        // point at a tier we never resolved.
        denial.Problem.Extensions.ShouldNotContainKey("minimumTier");
    }

    [Fact(DisplayName = "Given a well-formed but unregistered key, when EvaluateFeature runs, then it returns a 403 denial (fail closed)")]
    public void EvaluateFeatureDeniesUnknownKey()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);

        var denial = EditionGate.EvaluateFeature(registry, edition, "does-not-exist");

        denial.ShouldNotBeNull();
        denial.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        denial.Problem.Extensions!["code"].ShouldBe(RequiresFeatureFilter.FeatureUnavailableCode);
        denial.Problem.Extensions["feature"].ShouldBe("does-not-exist");
        denial.Problem.Extensions.ShouldNotContainKey("minimumTier");
    }

    [Fact(DisplayName = "Given usage under the cap, when EvaluateLimitAsync runs, then it returns null (no denial)")]
    public async Task EvaluateLimitAllowsUnderCapAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Limit(Arg.Any<Limit>()).Returns(10);
        var provider = Substitute.For<ILimitUsageProvider>();
        provider.LimitKey.Returns(LimitKey.Parse("projects"));
        provider.CurrentAsync(Arg.Any<CancellationToken>()).Returns(3);

        var denial = await EditionGate.EvaluateLimitAsync(
            registry,
            edition,
            [provider],
            "projects",
            CancellationToken.None);

        denial.ShouldBeNull();
    }

    [Fact(DisplayName = "Given usage at the cap, when EvaluateLimitAsync runs, then it returns a 403 denial with limit_exceeded code and cap/current extensions")]
    public async Task EvaluateLimitDeniesAtCapAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Limit(Arg.Any<Limit>()).Returns(10);
        var provider = Substitute.For<ILimitUsageProvider>();
        provider.LimitKey.Returns(LimitKey.Parse("projects"));
        provider.CurrentAsync(Arg.Any<CancellationToken>()).Returns(10);

        var denial = await EditionGate.EvaluateLimitAsync(
            registry,
            edition,
            [provider],
            "projects",
            CancellationToken.None);

        denial.ShouldNotBeNull();
        denial.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        denial.Problem.Extensions!["code"].ShouldBe(RequiresFeatureFilter.LimitExceededCode);
        denial.Problem.Extensions["limit"].ShouldBe("projects");
        denial.Problem.Extensions["cap"].ShouldBe(10);
        denial.Problem.Extensions["current"].ShouldBe(10);
    }

    [Fact(DisplayName = "Given usage over the cap, when EvaluateLimitAsync runs, then it returns a 403 denial")]
    public async Task EvaluateLimitDeniesOverCapAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Limit(Arg.Any<Limit>()).Returns(10);
        var provider = Substitute.For<ILimitUsageProvider>();
        provider.LimitKey.Returns(LimitKey.Parse("projects"));
        provider.CurrentAsync(Arg.Any<CancellationToken>()).Returns(11);

        var denial = await EditionGate.EvaluateLimitAsync(
            registry,
            edition,
            [provider],
            "projects",
            CancellationToken.None);

        denial.ShouldNotBeNull();
        denial.Problem.Extensions!["current"].ShouldBe(11);
    }

    [Fact(DisplayName = "Given a declared limit with no registered usage provider, when EvaluateLimitAsync runs, then it returns a 403 denial (fail closed) without throwing")]
    public async Task EvaluateLimitDeniesMissingProviderAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Limit(Arg.Any<Limit>()).Returns(10);

        var denial = await EditionGate.EvaluateLimitAsync(
            registry,
            edition,
            [], // no providers registered
            "projects",
            CancellationToken.None);

        denial.ShouldNotBeNull();
        denial.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        denial.Problem.Extensions!["code"].ShouldBe(RequiresFeatureFilter.LimitExceededCode);
        // A missing-provider denial reports current == cap (i.e. effectively exhausted)
        // so callers can show "10/10" rather than a 0/0 oddity.
        denial.Problem.Extensions["cap"].ShouldBe(10);
        denial.Problem.Extensions["current"].ShouldBe(10);
    }

    [Fact(DisplayName = "Given a malformed limit key, when EvaluateLimitAsync runs, then it returns a 403 denial (fail closed)")]
    public async Task EvaluateLimitDeniesMalformedKeyAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);

        var denial = await EditionGate.EvaluateLimitAsync(
            registry,
            edition,
            [],
            "BAD_KEY",
            CancellationToken.None);

        denial.ShouldNotBeNull();
        denial.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        denial.Problem.Extensions!["code"].ShouldBe(RequiresFeatureFilter.LimitExceededCode);
        denial.Problem.Extensions["limit"].ShouldBe("BAD_KEY");
    }

    [Fact(DisplayName = "Given a feature demand that denies, when the filter runs, then next() is not called and context.Result is a 403 ObjectResult")]
    public async Task FilterDeniesFeatureDemandAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Has(Arg.Any<Feature>()).Returns(false);
        var filter = new RequiresFeatureFilter(edition, registry, []);
        var context = NewResourceExecutingContext(
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = [new RequiresFeatureAttribute("multi-repo")] });

        await filter.OnResourceExecutionAsync(context, static () => throw new InvalidOperationException("next() must not be called when denied"));

        context.Result.ShouldNotBeNull();
        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        result.Value.ShouldBeOfType<ProblemDetails>();
    }

    [Fact(DisplayName = "Given a limit demand that denies, when the filter runs, then next() is not called and context.Result is a 403 ObjectResult")]
    public async Task FilterDeniesLimitDemandAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Limit(Arg.Any<Limit>()).Returns(1);
        var provider = Substitute.For<ILimitUsageProvider>();
        provider.LimitKey.Returns(LimitKey.Parse("projects"));
        provider.CurrentAsync(Arg.Any<CancellationToken>()).Returns(1);
        var filter = new RequiresFeatureFilter(edition, registry, [provider]);
        var context = NewResourceExecutingContext(
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = [new EnforceLimitAttribute("projects")] });

        await filter.OnResourceExecutionAsync(context, static () => throw new InvalidOperationException("next() must not be called when denied"));

        context.Result.ShouldNotBeNull();
        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact(DisplayName = "Given a feature demand that allows, when the filter runs, then next() is called and context.Result is null")]
    public async Task FilterAllowsFeatureDemandAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Has(Arg.Any<Feature>()).Returns(true);
        var filter = new RequiresFeatureFilter(edition, registry, []);
        var context = NewResourceExecutingContext(
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = [new RequiresFeatureAttribute("multi-repo")] });
        var nextCalled = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ResourceExecutedContext>(null!);
        });

        nextCalled.ShouldBeTrue();
        context.Result.ShouldBeNull();
    }

    [Fact(DisplayName = "Given no demands on the endpoint, when the filter runs, then next() is called and no evaluation runs")]
    public async Task FilterPassesThroughNoDemandAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = Substitute.For<IEditionCapabilityRegistry>();
        var filter = new RequiresFeatureFilter(edition, registry, []);
        var context = NewResourceExecutingContext(
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = [] });
        var nextCalled = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ResourceExecutedContext>(null!);
        });

        nextCalled.ShouldBeTrue();
        context.Result.ShouldBeNull();
        // The registry / edition must not have been consulted when there
        // is nothing to gate — the filter only walks them when a demand
        // is present, and an unconditional call would be a perf leak.
        edition.DidNotReceive().Has(Arg.Any<Feature>());
        edition.DidNotReceive().Limit(Arg.Any<Limit>());
    }

    [Fact(DisplayName = "Given two stacked feature demands, when the filter runs, then only the last (most specific) one is evaluated")]
    public async Task FilterLastFeatureDemandWinsAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Has(Arg.Any<Feature>()).Returns(true);
        var filter = new RequiresFeatureFilter(edition, registry, []);
        var context = NewResourceExecutingContext(
            new RouteData(),
            new ActionDescriptor
            {
                EndpointMetadata =
                [
                    new RequiresFeatureAttribute("multi-repo"),
                    new RequiresFeatureAttribute("agenteval"),
                ],
            });
        await filter.OnResourceExecutionAsync(context, static () => Task.FromResult<ResourceExecutedContext>(null!));

        // Last wins: endpoint metadata is ordered least to most specific
        // (controller attributes before action attributes), so the filter
        // evaluates the LAST entry — agenteval, not multi-repo.
        edition.DidNotReceive().Has(Arg.Is<Feature>(static f => f.Key.Value == "multi-repo"));
        edition.Received(1).Has(Arg.Is<Feature>(static f => f.Key.Value == "agenteval"));
        context.Result.ShouldBeNull();
    }

    [Fact(DisplayName = "Given both feature and limit demands with the feature denying, when the filter runs, then the feature denial surfaces (feature-first evaluation order)")]
    public async Task FilterFeatureDemandEvaluatedBeforeLimitDemandAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = NewRegistry(out _, out _);
        edition.Has(Arg.Any<Feature>()).Returns(false);
        // Limit would normally pass — but feature denial should short-circuit first.
        edition.Limit(Arg.Any<Limit>()).Returns(10);
        var provider = Substitute.For<ILimitUsageProvider>();
        provider.LimitKey.Returns(LimitKey.Parse("projects"));
        provider.CurrentAsync(Arg.Any<CancellationToken>()).Returns(0);
        var filter = new RequiresFeatureFilter(edition, registry, [provider]);
        var context = NewResourceExecutingContext(
            new RouteData(),
            new ActionDescriptor
            {
                EndpointMetadata =
                [
                    new RequiresFeatureAttribute("multi-repo"),
                    new EnforceLimitAttribute("projects"),
                ],
            });

        await filter.OnResourceExecutionAsync(context, static () => throw new InvalidOperationException("next() must not be called when denied"));

        context.Result.ShouldNotBeNull();
        var problem = context.Result.ShouldBeOfType<ObjectResult>().Value.ShouldBeOfType<ProblemDetails>();
        problem.Extensions!["code"].ShouldBe(RequiresFeatureFilter.FeatureUnavailableCode);
        // The limit provider must not have been queried — feature denial short-circuits.
        await provider.DidNotReceive().CurrentAsync(Arg.Any<CancellationToken>());
    }

    private static IEditionCapabilityRegistry NewRegistry(out Feature multiRepo, out Limit projects)
    {
        // Use the real catalog entries so the test exercises the same
        // well-formedness rules production code does.
        multiRepo = Features.MultiRepo;
        projects = Limits.Projects;
        return new EditionCapabilityRegistry();
    }

    private static ResourceExecutingContext NewResourceExecutingContext(RouteData routeData, ActionDescriptor actionDescriptor)
    {
        var httpContext = Substitute.For<HttpContext>();
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor, new ModelStateDictionary());
        return new ResourceExecutingContext(actionContext, [], []);
    }
}
