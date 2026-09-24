using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// All Auth integration tests share one host (the bootstrap admin cookie
/// session, the rate-limit partitions on <c>/auth/login</c>) — running
/// them in parallel thrashes the login bucket. The collection disables
/// parallelisation across all four test classes; sequential execution
/// also makes the docker-compose traffic predictable.
/// </summary>
/// <remarks>
/// WS2 (add-agentic-test-contour): <c>HostAuthServer</c> owns a
/// <c>Comuki.Host.Testing.Fixtures.PostgresCollectionFixture</c> by
/// containment (a plain field, its lifecycle methods called from
/// <c>HostAuthServer</c>'s own), not as a second xUnit collection fixture
/// on this definition — xUnit v3's collection-fixture resolver does not
/// thread one declared <c>ICollectionFixture&lt;T&gt;</c> into another's
/// constructor (verified empirically: declaring both here left
/// <c>HostAuthServer</c>'s <c>PostgresCollectionFixture</c> parameter
/// unresolved at run time). Containment gets the same one-container-per-
/// collection result with no dependency on that unsupported resolution
/// order. No <c>ResetDatabaseAsync</c> call was added — Auth's suite
/// already shares one host/database across all four classes with no
/// per-test reset (tests scope themselves by generated ids), so this
/// conversion only swaps who owns the container, matching prior behavior.
/// </remarks>
[CollectionDefinition(nameof(AuthIntegrationCollection), DisableParallelization = true)]
public sealed class AuthIntegrationCollection : ICollectionFixture<HostAuthServer>
{
}
