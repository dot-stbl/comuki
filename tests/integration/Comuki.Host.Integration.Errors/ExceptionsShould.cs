using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Errors;

/// <summary>
/// Central <c>ProviderExceptionHandler</c> mapping (issue #17): every
/// typed exception thrown in the host composition must surface as RFC
/// 9457 <c>application/problem+json</c> with the documented status +
/// stable <c>code</c> extension. Per the handler's contract, the
/// <c>detail</c> must never include stack/PII (log-only).
/// </summary>
public sealed class ExceptionsShould(HostErrorServer server) : IClassFixture<HostErrorServer>
{
    private const string ProblemMediaType = "application/problem+json";

    [Fact(DisplayName = "Given a base ProviderException, when the endpoint throws, then it answers 502 with provider.* code")]
    public async Task ProviderExceptionMapsTo502Async()
    {
        var response = await server.Client.GetAsync("/test/throw/provider-base", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemMediaType);

        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadGateway);
        problem.GetProperty("title").GetString().ShouldBe("Upstream unavailable");
        problem.GetProperty("code").GetString().ShouldBe("provider.network_error");
    }

    [Fact(DisplayName = "Given a ProviderTimeoutException, when the endpoint throws, then it answers 504 with provider.timeout code")]
    public async Task ProviderTimeoutMapsTo504Async()
    {
        var response = await server.Client.GetAsync("/test/throw/provider-timeout", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemMediaType);

        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.GatewayTimeout);
        problem.GetProperty("title").GetString().ShouldBe("Upstream timeout");
        problem.GetProperty("code").GetString().ShouldBe("provider.timeout");
    }

    [Fact(DisplayName = "Given a ProviderNotFoundException, when the endpoint throws, then it answers 404 with the carried code")]
    public async Task ProviderNotFoundMapsTo404Async()
    {
        var response = await server.Client.GetAsync("/test/throw/provider-not-found", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemMediaType);

        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        problem.GetProperty("title").GetString().ShouldBe("Resource not found");
        problem.GetProperty("code").GetString().ShouldBe("provider.not_found");
    }

    [Fact(DisplayName = "Given a DomainException, when the endpoint throws, then it answers 422 with the carried code")]
    public async Task DomainExceptionMapsTo422Async()
    {
        var response = await server.Client.GetAsync("/test/throw/domain", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemMediaType);

        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.UnprocessableEntity);
        problem.GetProperty("title").GetString().ShouldBe("Domain rule violated");
        problem.GetProperty("code").GetString().ShouldBe("domain.conflict");
    }

    [Fact(DisplayName = "Given an unrecognised exception, when the endpoint throws, then it answers 500 with a generic code")]
    public async Task UnknownExceptionMapsTo500Async()
    {
        var response = await server.Client.GetAsync("/test/throw/unknown", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemMediaType);

        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.InternalServerError);
        problem.GetProperty("code").GetString().ShouldBe("internal.error");
        var detail = problem.GetProperty("detail").GetString() ?? string.Empty;
        detail.ShouldNotContain("kaboom");
    }

    [Fact(DisplayName = "Given a DomainException subclass, when the endpoint throws, then the type-tree walk still classifies it as 422")]
    public async Task DomainExceptionSubclassMapsTo422Async()
    {
        var response = await server.Client.GetAsync("/test/throw/domain-subclass", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemMediaType);

        var problem = await ReadProblemAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.UnprocessableEntity);
        problem.GetProperty("code").GetString().ShouldBe("subclass.of.domain");
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
    }
}
