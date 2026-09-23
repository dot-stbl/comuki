using Xunit;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// One real Podman-provisioned container per test in this collection —
/// disabled parallelization so two scenarios never race for the same
/// <see cref="AgentLoopHost"/> port/webhook/compute-provider state.
/// </summary>
[CollectionDefinition(nameof(AgentLoopCollection), DisableParallelization = true)]
public sealed class AgentLoopCollection : ICollectionFixture<AgentLoopHost>
{
}
