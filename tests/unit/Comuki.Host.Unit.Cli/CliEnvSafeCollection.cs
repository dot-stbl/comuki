using Xunit;

namespace Comuki.Host.Unit.Cli;

/// <summary>
/// xUnit collection that serialises every test that touches process-global
/// state (env vars, temp config files) through the comuki CLI branches.
/// </summary>
[CollectionDefinition(nameof(CliEnvSafeCollection), DisableParallelization = true)]
public sealed class CliEnvSafeCollection;
