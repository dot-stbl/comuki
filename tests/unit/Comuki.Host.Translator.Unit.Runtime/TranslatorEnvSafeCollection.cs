using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// xUnit collection that serialises every test that touches the
/// <c>COMUKI_*</c> process-global env vars the
/// <see cref="TranslatorEnvironment"/> helper reads. Without this gate,
/// <see cref="TranslatorEnvironmentSnapshotShould"/>'s setup/dispose
/// would race <see cref="ProfilesProviderShould"/>'s snapshot assertions
/// and the dev-machine's leaked env vars would surface as flaky
/// failures. Same pattern as <c>CliEnvSafeCollection</c> in the host CLI
/// tests; lives next to the only two test classes that need it.
/// </summary>
[CollectionDefinition(nameof(TranslatorEnvSafeCollection), DisableParallelization = true)]
public sealed class TranslatorEnvSafeCollection;
