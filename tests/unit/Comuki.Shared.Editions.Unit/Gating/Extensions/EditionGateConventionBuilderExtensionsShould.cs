using System.Linq;
using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Gating;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Gating.Extensions;

/// <summary>
/// EditionGateConventionBuilderExtensions tests — the minimal-API fluent
/// form that adds the same <see cref="RequiresFeatureAttribute"/> /
/// <see cref="EnforceLimitAttribute"/> shape to endpoint metadata that
/// the attribute-on-method form adds directly. The contract is "both
/// paths land the same metadata" (issue #164 §3a task 4.3), so the
/// tests here assert that the metadata is added verbatim and that the
/// convention-builder is returned (for chaining).
/// </summary>
public sealed class EditionGateConventionBuilderExtensionsShould
{
    [Fact(DisplayName = "Given a Feature, when RequireFeature is called, then it adds the feature's key to the endpoint metadata as a RequiresFeatureAttribute")]
    public void RequireFeatureAddsAttributeWithFeatureKey()
    {
        var builder = Substitute.For<IEndpointConventionBuilder>();
        var endpointBuilder = new TestEndpointBuilder();
        Action<EndpointBuilder>? captured = null;
        builder.Add(Arg.Do<Action<EndpointBuilder>>(action => captured = action));

        var returned = builder.RequireFeature(Features.MultiRepo);

        returned.ShouldBe(builder);
        captured.ShouldNotBeNull();
        captured!(endpointBuilder);
        endpointBuilder.Metadata.OfType<RequiresFeatureAttribute>().Single().FeatureKey.ShouldBe("multi-repo");
    }

    [Fact(DisplayName = "Given a Limit, when EnforceLimit is called, then it adds the limit's key to the endpoint metadata as an EnforceLimitAttribute")]
    public void EnforceLimitAddsAttributeWithLimitKey()
    {
        var builder = Substitute.For<IEndpointConventionBuilder>();
        var endpointBuilder = new TestEndpointBuilder();
        Action<EndpointBuilder>? captured = null;
        builder.Add(Arg.Do<Action<EndpointBuilder>>(action => captured = action));

        var returned = builder.EnforceLimit(Limits.Projects);

        returned.ShouldBe(builder);
        captured.ShouldNotBeNull();
        captured!(endpointBuilder);
        endpointBuilder.Metadata.OfType<EnforceLimitAttribute>().Single().LimitKey.ShouldBe("projects");
    }

    /// <summary>Minimal concrete <see cref="EndpointBuilder"/> — the base class is abstract and only <see cref="Build"/> needs a body, never called here.</summary>
    private sealed class TestEndpointBuilder : EndpointBuilder
    {
        public override Endpoint Build()
        {
            throw new NotSupportedException("test-only builder; Build() is never exercised by these tests");
        }
    }
}
