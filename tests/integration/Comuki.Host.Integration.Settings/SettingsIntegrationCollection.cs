using Comuki.Host.Testing.Fixtures;
using Xunit;

namespace Comuki.Host.Integration.Settings;

/// <summary>
/// One shared Postgres for the whole Settings suite (WS3), never in
/// parallel — mirrors <c>Comuki.Host.Integration.Costs.CostsIntegrationCollection</c>'s
/// rationale: two Testcontainers hosts racing the same bootstrap-admin
/// seed on one daemon starve the slower boot mid-start.
/// </summary>
[CollectionDefinition(nameof(SettingsIntegrationCollection), DisableParallelization = true)]
public sealed class SettingsIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;
