using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Workers.Api;

/// <summary>
/// Worker-runtime authentication helpers. Extracted from
/// <see cref="WorkerEndpoints"/> so the endpoint class holds only
/// orchestration; per <c>class-layout-and-tooling.md §1a</c> no
/// <c>private</c> business logic lives on a production class.
/// </summary>
internal static class WorkerEndpointHelpers
{
    /// <summary>Extracts the authenticated worker id from the request headers, or returns <c>null</c>.</summary>
    /// <param name="authenticator"></param>
    /// <param name="httpContext"></param>
    public static WorkerId? AuthenticateWorker(WorkerTokenAuthenticator authenticator, HttpContext httpContext)
    {
        return authenticator.Authenticate(WorkerTokenHeaders.TryGetFromHttp(httpContext.Request.Headers));
    }
}
