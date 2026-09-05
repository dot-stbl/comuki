using Comuki.Modules.Identity.Domain.Oidc;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Store contract: SaveAsync round-trip, ConsumeAsync deletes the row in
/// one call (single-use), ConsumeAsync on a stale row reads as null.
/// DeleteExpiredAsync uses <c>ExecuteDeleteAsync</c> which is a relational
/// provider affordance — it is covered by the integration suite, not here.
/// </summary>
public sealed class OidcStateStoreShould
{
    private static readonly DateTimeOffset testNow = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static IdentityDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName: $"oidc-states-{Guid.NewGuid()}")
            .ConfigureWarnings(static warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new IdentityDbContext(options);
    }

    [Fact(DisplayName = "Given a saved state, when SaveAsync then ConsumeAsync, then the round-trip carries provider, verifier and returnTo")]
    public async Task RoundTripsStateAsync()
    {
        await using var db = NewDbContext();
        var store = new OidcStateStore(db, new FakeTimeProvider(testNow));
        var saved = OidcState.Create(
            "keycloak",
            "verifier-abc",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            "/runs",
            testNow,
            TimeSpan.FromMinutes(5));
        await store.SaveAsync(saved, TestContext.Current.CancellationToken);

        var consumed = await store.ConsumeAsync(saved.Id, TestContext.Current.CancellationToken);

        consumed.ShouldNotBeNull();
        consumed.Provider.ShouldBe("keycloak");
        consumed.CodeVerifier.ShouldBe("verifier-abc");
        consumed.CodeChallengeMethod.ShouldBe("S256");
        consumed.RedirectUri.ShouldBe("https://app.example.com/api/v1/auth/oidc/callback");
        consumed.ReturnTo.ShouldBe("/runs");
    }

    [Fact(DisplayName = "Given a consumed state, when ConsumeAsync runs again, then the second call reads null (single-use)")]
    public async Task ConsumeIsSingleUseAsync()
    {
        await using var db = NewDbContext();
        var store = new OidcStateStore(db, new FakeTimeProvider(testNow));
        var state = OidcState.Create(
            "keycloak",
            "verifier-once",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            null,
            testNow,
            TimeSpan.FromMinutes(5));
        await store.SaveAsync(state, TestContext.Current.CancellationToken);

        var first = await store.ConsumeAsync(state.Id, TestContext.Current.CancellationToken);
        var second = await store.ConsumeAsync(state.Id, TestContext.Current.CancellationToken);

        first.ShouldNotBeNull();
        second.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a missing state id, when ConsumeAsync runs, then it reads null without throwing")]
    public async Task ConsumeMissingReturnsNullAsync()
    {
        await using var db = NewDbContext();
        var store = new OidcStateStore(db, new FakeTimeProvider(testNow));

        var consumed = await store.ConsumeAsync(OidcStateId.New(), TestContext.Current.CancellationToken);

        consumed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a stored state whose ExpiresAt has passed, when ConsumeAsync runs, then null is returned even though the row exists")]
    public async Task ConsumeExpiredReturnsNullAsync()
    {
        await using var db = NewDbContext();
        var store = new OidcStateStore(db, new FakeTimeProvider(testNow));
        var state = OidcState.Create(
            "keycloak",
            "expired-verifier",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            null,
            testNow.AddMinutes(-10),
            TimeSpan.FromMinutes(5));
        await store.SaveAsync(state, TestContext.Current.CancellationToken);

        var consumed = await store.ConsumeAsync(state.Id, TestContext.Current.CancellationToken);

        consumed.ShouldBeNull();
    }
}

/// <summary>
/// Deterministic clock for expiry tests — the store reads time exclusively
/// through the injected <see cref="TimeProvider" />.
/// </summary>
/// <param name="initial">The fixed reading returned by <see cref="GetUtcNow" />.</param>
internal sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private readonly DateTimeOffset utcNow = initial;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
