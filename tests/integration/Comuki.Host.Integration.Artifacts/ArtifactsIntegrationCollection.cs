using Comuki.Host.Testing.Fixtures;
using Xunit;

namespace Comuki.Host.Integration.Artifacts;

/// <summary>
/// One shared Postgres for the whole Artifacts suite (WS2 follow-up), never
/// in parallel: two full-host Testcontainers boots on the same Docker daemon
/// starve each other's bootstrap-admin seed, the same contract
/// <c>CostsIntegrationCollection</c> documents.
/// </summary>
/// <remarks>
/// MinIO is intentionally NOT a collection fixture — every test class still
/// owns its own <see cref="Testcontainers.Minio.MinioContainer"/> because
/// each test boots its own host and the bucket-initializer is a per-host
/// background worker that this fixture has no view of. Migrating MinIO to
/// a collection fixture is its own work item; out of scope here.
/// </remarks>
[CollectionDefinition(nameof(ArtifactsIntegrationCollection), DisableParallelization = true)]
public sealed class ArtifactsIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;
