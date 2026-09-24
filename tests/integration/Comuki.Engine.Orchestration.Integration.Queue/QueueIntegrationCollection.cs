using Comuki.Host.Testing.Fixtures;
using Xunit;

namespace Comuki.Engine.Orchestration.Integration.Queue;

/// <summary>
/// One shared Postgres for the whole queue suite (WS2 follow-up), never in
/// parallel: the same one-container-per-collection contract
/// <c>CostsIntegrationCollection</c> documents.
/// </summary>
/// <remarks>
/// This suite is the structurally awkward case — an abstract
/// <c>QueueDatabase</c> base class owned the per-test container and was
/// inherited by two test classes (no <c>[Collection]</c> attribute before
/// this commit, so each class got its own container per <c>[Fact]</c>). The
/// base now takes the fixture via primary constructor and forwards it to
/// both subclasses; the <see cref="PostgresCollectionFixture.ResetDatabaseAsync"/>
/// call at the top of <c>InitializeAsync</c> is load-bearing here — every
/// test seeds fresh runs/items and asserts on exact claim/queue behavior,
/// so cross-test rows would silently break ordering and count assertions.
/// </remarks>
[CollectionDefinition(nameof(QueueIntegrationCollection), DisableParallelization = true)]
public sealed class QueueIntegrationCollection : ICollectionFixture<PostgresCollectionFixture>;
