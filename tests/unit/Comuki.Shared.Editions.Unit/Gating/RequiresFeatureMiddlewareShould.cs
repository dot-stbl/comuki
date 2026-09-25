using System.Text.Json;
using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Registry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Gating;

/// <summary>
/// RequiresFeatureMiddleware tests — the minimal-API counterpart to
/// <see cref="RequiresFeatureFilter"/>. The shared decision is covered
/// by <see cref="RequiresFeatureFilterShould"/> (one gate, one set of
/// static-method tests); here we cover the middleware's pipeline shape:
/// endpoint-metadata walking, the 403 / <c>problem+json</c> write path,
/// the pass-through when no demand is present, the
/// <see cref="ILimitUsageProvider"/> short-circuit, and the
/// feature-first evaluation order.
/// </summary>
public sealed class RequiresFeatureMiddlewareShould
{
    [Fact(DisplayName = "Given a community edition without the feature, when InvokeAsync runs, then it writes a 403 problem+json and does not call next")]
    public async Task InvokeAsyncWrites403AndShortCircuitsAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = new EditionCapabilityRegistry();
        edition.Has(Arg.Any<Catalog.Feature>()).Returns(false);
        var middleware = new RequiresFeatureMiddleware(ThrowingNextAsync);
        var context = NewContext(
            new RequiresFeatureAttribute("multi-repo"),
            services: BuildServices(edition, registry, []));

        await middleware.InvokeAsync(context, edition, registry, []);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        // WriteAsJsonAsync(value) — the 1-arg overload that the brief
        // mandates ("exactly like RequiresPermissionMiddleware") —
        // overrides ContentType to "application/json; charset=utf-8",
        // so we do not assert ContentType here. The status code + body
        // shape carry the 403 / problem+json contract.
        var body = await ReadBodyAsync(context);
        body.ShouldNotBeNullOrEmpty();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonSerializerOptions.Web)!;
        problem.Status.ShouldBe(StatusCodes.Status403Forbidden);
        GetExtension(problem, "code").ShouldBe(EditionDenialBuilder.FeatureUnavailableCode);
        GetExtension(problem, "feature").ShouldBe("multi-repo");
    }

    [Fact(DisplayName = "Given a paid edition that covers the feature, when InvokeAsync runs, then it calls next() and writes nothing")]
    public async Task InvokeAsyncCallsNextOnAllowedFeatureAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = new EditionCapabilityRegistry();
        edition.Has(Arg.Any<Catalog.Feature>()).Returns(true);
        var nextCalled = false;
        var middleware = new RequiresFeatureMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = NewContext(
            new RequiresFeatureAttribute("multi-repo"),
            services: BuildServices(edition, registry, []));

        await middleware.InvokeAsync(context, edition, registry, []);

        nextCalled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact(DisplayName = "Given a limit demand that denies (usage at cap), when InvokeAsync runs, then it writes a 403 problem+json with limit_exceeded extensions")]
    public async Task InvokeAsyncWrites403ForLimitDenialAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = new EditionCapabilityRegistry();
        edition.Limit(Arg.Any<Catalog.Limit>()).Returns(1);
        var provider = Substitute.For<ILimitUsageProvider>();
        provider.LimitKey.Returns(LimitKey.Parse("projects"));
        provider.CurrentAsync(Arg.Any<CancellationToken>()).Returns(1);
        var middleware = new RequiresFeatureMiddleware(ThrowingNextAsync);
        var context = NewContext(
            new EnforceLimitAttribute("projects"),
            services: BuildServices(edition, registry, [provider]));

        await middleware.InvokeAsync(context, edition, registry, [provider]);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        var body = await ReadBodyAsync(context);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonSerializerOptions.Web)!;
        GetExtension(problem, "code").ShouldBe(EditionDenialBuilder.LimitExceededCode);
        GetExtension(problem, "limit").ShouldBe("projects");
        GetExtension(problem, "cap").ShouldBe("1");
        GetExtension(problem, "current").ShouldBe("1");
    }

    [Fact(DisplayName = "Given no demands on the endpoint, when InvokeAsync runs, then it calls next() and the edition/registry are never consulted")]
    public async Task InvokeAsyncPassesThroughOnNoDemandAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = Substitute.For<IEditionCapabilityRegistry>();
        var nextCalled = false;
        var middleware = new RequiresFeatureMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = NewContext(
            demand: null,
            services: BuildServices(edition, registry, []));

        await middleware.InvokeAsync(context, edition, registry, []);

        nextCalled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        edition.DidNotReceive().Has(Arg.Any<Catalog.Feature>());
        edition.DidNotReceive().Limit(Arg.Any<Catalog.Limit>());
    }

    [Fact(DisplayName = "Given both feature and limit demands with the feature denying, when InvokeAsync runs, then the feature denial is written and the limit provider is not queried")]
    public async Task InvokeAsyncFeatureDenialShortCircuitsBeforeLimitAsync()
    {
        var edition = Substitute.For<IEdition>();
        var registry = new EditionCapabilityRegistry();
        edition.Has(Arg.Any<Catalog.Feature>()).Returns(false);
        edition.Limit(Arg.Any<Catalog.Limit>()).Returns(10);
        var provider = Substitute.For<ILimitUsageProvider>();
        provider.LimitKey.Returns(LimitKey.Parse("projects"));
        provider.CurrentAsync(Arg.Any<CancellationToken>()).Returns(0);
        var middleware = new RequiresFeatureMiddleware(ThrowingNextAsync);
        var context = NewContext(
            new CompositeDemand(
                new RequiresFeatureAttribute("multi-repo"),
                new EnforceLimitAttribute("projects")),
            services: BuildServices(edition, registry, [provider]));

        await middleware.InvokeAsync(context, edition, registry, [provider]);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        var body = await ReadBodyAsync(context);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonSerializerOptions.Web)!;
        GetExtension(problem, "code").ShouldBe(EditionDenialBuilder.FeatureUnavailableCode);
        await provider.DidNotReceive().CurrentAsync(Arg.Any<CancellationToken>());
    }

    private static HttpContext NewContext(Attribute? demand, IServiceProvider services)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
        };

        // DefaultHttpContext ships Stream.Null as its Body, which silently
        // discards writes; assign a MemoryStream so WriteAsJsonAsync has
        // somewhere to land its body and the assertion can read it back.
        context.Response.Body = new MemoryStream();

        if (demand is not null)
        {
            var metadata = demand is CompositeDemand composite
                ? new EndpointMetadataCollection(composite.All)
                : new EndpointMetadataCollection(demand);
            context.SetEndpoint(new Endpoint(null, metadata, "test"));
        }
        else
        {
            // No endpoint metadata at all — middleware must short-circuit
            // without consulting the edition/registry.
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "test"));
        }

        return context;
    }

    private static IServiceProvider BuildServices(
        IEdition edition,
        IEditionCapabilityRegistry registry,
        IEnumerable<ILimitUsageProvider> providers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(edition);
        services.AddSingleton(registry);
        foreach (var provider in providers)
        {
            services.AddSingleton(provider);
        }

        return services.BuildServiceProvider();
    }

    private static Task ThrowingNextAsync(HttpContext _)
    {
        throw new InvalidOperationException("next() must not be called when the gate denies.");
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Extension values round-trip through STJ as <see cref="JsonElement"/>;
    /// unwrap to a string for Shouldly equality assertions. The
    /// <see cref="ProblemDetails.Extensions"/> dictionary holds object
    /// values; STJ serialises string values as JSON strings and numeric
    /// values as JSON numbers, so the element kind differs per call site.
    /// </summary>
    private static string GetExtension(ProblemDetails problem, string key)
    {
        return problem.Extensions[key] switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetRawText(),
            string direct => direct,
            _ => problem.Extensions[key]?.ToString() ?? string.Empty,
        };
    }

    /// <summary>Wraps multiple demands so a single endpoint can carry both a feature and a limit demand.</summary>
    private sealed class CompositeDemand(params Attribute[] all) : Attribute
    {
        public Attribute[] All { get; } = all;
    }
}
