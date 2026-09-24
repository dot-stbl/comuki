using Xunit;

namespace Comuki.Host.Integration.Smoke;

/// <summary>
/// One shared <see cref="SmokeHostServer"/> (and its contained Postgres + MinIO)
/// for the whole Smoke suite (WS2 follow-up) — replaces five separate
/// IClassFixture&lt;SmokeHostServer&gt; instances (one per test class, each
/// booting its own Postgres + MinIO + host) with a single collection-shared
/// instance. <c>DisableParallelization = true</c>: never run two of this
/// suite's full-host stacks at once (there's only one now anyway, but this
/// also serialises the 5 classes' tests against each other, since
/// <c>BootShould.SeedPlatformFactsAtBootAsync</c>'s "exactly 3
/// <c>platform.*</c> facts" assertion depends on nothing else in the suite
/// concurrently mutating <c>memory.memory_facts</c>).
/// </summary>
/// <remarks>
/// Matched shape to
/// <c>Comuki.Host.Integration.Auth.AuthIntegrationCollection</c>: the
/// Postgres container lives inside <see cref="SmokeHostServer"/> as an
/// owned <c>PostgresCollectionFixture</c> (containment — xUnit v3 does
/// not thread one declared <c>ICollectionFixture&lt;T&gt;</c> into another's
/// constructor, so the contained-field pattern is the documented escape).
/// No <c>ResetDatabaseAsync</c> is added — Smoke, like Auth and Intake
/// before it, shares one host/database across the whole suite with no
/// per-test reset (tests scope themselves by generated ids, and the boot
/// seeder only writes the three <c>platform.*</c> facts once on the single
/// shared host boot). This matches the no-reset precedent, not the
/// per-test-reset precedent (Costs/Queue/Artifacts/Filtering.Ef/etc.),
/// because the project shares one host across the whole suite, not one
/// host per test class with per-test resets.
/// </remarks>
[CollectionDefinition(nameof(SmokeIntegrationCollection), DisableParallelization = true)]
public sealed class SmokeIntegrationCollection : ICollectionFixture<SmokeHostServer>
{
}
