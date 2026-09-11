using Xunit;

namespace Comuki.Migrator.Unit;

/// <summary>
/// xUnit collection that serialises every test that touches process-global
/// state (env vars + the COMUKI_CONFIG_PATH override). Without this gate,
/// two parallel tests that both rewrite the same env var race and one
/// reads the other's scoped config.toml.
/// </summary>
[CollectionDefinition(nameof(MigratorEnvSafeCollection), DisableParallelization = true)]
public sealed class MigratorEnvSafeCollection;
