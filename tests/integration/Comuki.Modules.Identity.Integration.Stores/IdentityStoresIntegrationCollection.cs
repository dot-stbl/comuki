using Comuki.Host.Testing.Fixtures;
using Xunit;

namespace Comuki.Modules.Identity.Integration.Stores;

/// <summary>
/// One shared Postgres for the whole identity-stores suite (WS2 follow-up),
/// never in parallel: the same one-container-per-collection contract
/// <c>CostsIntegrationCollection</c> documents.
/// </summary>
[CollectionDefinition(nameof(IdentityStoresIntegrationCollection), DisableParallelization = true)]
public sealed class IdentityStoresIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;
