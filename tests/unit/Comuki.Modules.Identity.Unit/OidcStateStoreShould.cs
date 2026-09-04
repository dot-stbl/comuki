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
        var store = new OidcStateStore(db);
        var now = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        var saved = OidcState.Create(
            "keycloak",
            "verifier-abc",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            "/runs",
            now,
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
        var store = new OidcStateStore(db);
        var state = OidcState.Create(
            "keycloak",
            "verifier-once",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            null,
            DateTimeOffset.UtcNow,
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
        var store = new OidcStateStore(db);

        var consumed = await store.ConsumeAsync(OidcStateId.New(), TestContext.Current.CancellationToken);

        consumed.ShouldBeNull();
    }
}
