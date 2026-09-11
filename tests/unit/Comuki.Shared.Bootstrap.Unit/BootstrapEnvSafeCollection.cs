using Xunit;

namespace Comuki.Shared.Bootstrap.Unit;

/// <summary>
/// xUnit collection that serialises every test that touches process-global
/// state (env vars). Without this gate, two parallel tests that rewrite the
/// same env var race.
/// </summary>
[CollectionDefinition(nameof(BootstrapEnvSafeCollection), DisableParallelization = true)]
public sealed class BootstrapEnvSafeCollection;
