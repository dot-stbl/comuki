using Xunit;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// One <see cref="RealPiFakeModelHost"/> (Postgres + webhook, no Docker)
/// and one <see cref="RealPiInstallation"/> (the vendored real pi binary,
/// installed once) per test run — disabled parallelization for the same
/// reason as <see cref="AgentLoopCollection"/>: two scenarios must never
/// race for the same host's port/webhook state, and this harness also
/// mutates process-global pi environment variables for the duration of
/// each translator cycle (see <c>RealPiFakeModelHarness.StampAmbientPiEnvironment</c>).
/// </summary>
[CollectionDefinition(nameof(RealPiFakeModelCollection), DisableParallelization = true)]
public sealed class RealPiFakeModelCollection : ICollectionFixture<RealPiFakeModelHost>, ICollectionFixture<RealPiInstallation>
{
}
