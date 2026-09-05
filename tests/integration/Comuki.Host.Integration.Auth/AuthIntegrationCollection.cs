using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// All Auth integration tests share one host (the bootstrap admin cookie
/// session, the rate-limit partitions on <c>/auth/login</c>) — running
/// them in parallel thrashes the login bucket. The collection disables
/// parallelisation across all four test classes; sequential execution
/// also makes the docker-compose traffic predictable.
/// </summary>
[CollectionDefinition(nameof(AuthIntegrationCollection), DisableParallelization = true)]
public sealed class AuthIntegrationCollection : ICollectionFixture<HostAuthServer>
{
}
