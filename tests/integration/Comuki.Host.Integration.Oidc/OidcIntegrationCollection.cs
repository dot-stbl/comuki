using Xunit;

namespace Comuki.Host.Integration.Oidc;

/// <summary>
/// One shared <see cref="HostOidcServer"/> (and its contained Postgres)
/// for the whole OIDC suite (WS2 follow-up) — replaces two separate
/// <c>IClassFixture&lt;HostOidcServer&gt;</c> instances (one per test class,
/// each booting its own Postgres + Keycloak + host) with a single
/// collection-shared instance. <c>DisableParallelization = true</c>: never
/// run two of this suite's full-host stacks at once (there's only one now
/// anyway).
/// </summary>
/// <remarks>
/// <para>
/// This project is different from every other conversion in this rollout:
/// it also boots a real Keycloak container alongside Postgres. Keycloak is
/// NOT part of this conversion — its lifecycle, image, and realm-import
/// wiring stay exactly as-is inside <see cref="HostOidcServer"/> (still
/// its own field, its own <c>StartAsync</c>/<c>DisposeAsync</c>). Only the
/// Postgres half changes.
/// </para>
/// <para>
/// Matched shape to
/// <c>Comuki.Host.Integration.Smoke.SmokeIntegrationCollection</c>: the
/// Postgres container lives inside <see cref="HostOidcServer"/> as an
/// owned <c>PostgresCollectionFixture</c> (containment — xUnit v3 does
/// not thread one declared <c>ICollectionFixture&lt;T&gt;</c> into
/// another's constructor, so the contained-field pattern is the documented
/// escape). No <c>ResetDatabaseAsync</c> is added — Oidc, like Smoke /
/// Auth / Intake before it, shares one host/database across the whole
/// suite with no per-test reset: <c>OidcState.Id</c> is a UUIDv7 (no
/// collision across runs of <c>OidcStateSweeperShould</c>'s fixed
/// <c>"verifier-expired"</c> / <c>"verifier-fresh"</c> verifiers — only
/// the primary key is unique, <c>code_verifier</c> is not), and the
/// linker test's <c>LinkAsync</c> is idempotent on the second call
/// (first call provisions user + link, second call finds the existing
/// link and returns <c>Created = false</c>; the test asserts both).
/// </para>
/// </remarks>
[CollectionDefinition(nameof(OidcIntegrationCollection), DisableParallelization = true)]
public sealed class OidcIntegrationCollection : ICollectionFixture<HostOidcServer>
{
}
