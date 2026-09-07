using Xunit;

namespace Comuki.Migrator.Unit;

/// <summary>
/// xUnit collection that serialises every test that touches process-global
/// state (env vars + <c>appsettings.json</c>). Without this gate, two
/// parallel tests that both rewrite <c>appsettings.json</c> race and one
/// reads the other's staged file.
/// </summary>
[CollectionDefinition("MigratorEnvSafe", DisableParallelization = true)]
public sealed class MigratorEnvSafeCollection;
