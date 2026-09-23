using Comuki.Host.Testing.Fixtures;
using Xunit;

namespace Comuki.Modules.Knowledge.Integration;

/// <summary>
/// One shared Postgres for the whole Knowledge integration suite (WS3),
/// migrated once (pgvector extension + <c>knowledge.*</c> schema included
/// via <c>Comuki.Shared.Migrations.Targets.MigrationTargets.All</c>) and
/// reset to empty before every test via
/// <see cref="PostgresCollectionFixture.ResetDatabaseAsync"/>.
/// </summary>
[CollectionDefinition(nameof(KnowledgeIntegrationCollection), DisableParallelization = true)]
public sealed class KnowledgeIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;
