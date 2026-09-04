using Comuki.Host.Workers;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Oidc;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Oidc;

/// <summary>
/// Sweep contract (issue #4 tail) against real Postgres: insert an
/// expired state row + a fresh one through the production
/// <see cref="IOidcStateStore"/>, run one cycle of the host's
/// <see cref="OidcStateSweeper"/>, and verify the table now holds only
/// the fresh row. The expired row is forged by saving a normally-shaped
/// state and then back-dating its <c>ExpiresAt</c> inside the same
/// EF scope — the same <see cref="IdentityDbContext"/> tracks the
/// entity across both writes.
/// </summary>
public sealed class OidcStateSweeperShould(HostOidcServer server) : IClassFixture<HostOidcServer>
{
    [Fact(DisplayName = "Given an expired state row and a fresh one, when the host's sweeper runs one cycle, then only the fresh row remains")]
    public async Task SweepDeletesExpiredKeepsFreshAsync()
    {
        await using var setup = server.Services.CreateAsyncScope();
        var store = setup.ServiceProvider.GetRequiredService<IOidcStateStore>();
        var db = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var expired = OidcState.Create(
                    "keycloak",
                    "verifier-expired",
                    "S256",
                    "https://app.example.com/api/v1/auth/oidc/callback",
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(-10),
                    TimeSpan.FromMinutes(5));
        await store.SaveAsync(expired, TestContext.Current.CancellationToken);
        db.Entry(expired).Property(static state => state.ExpiresAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var fresh = OidcState.Create(
            "keycloak",
            "verifier-fresh",
            "S256",
            "https://app.example.com/api/v1/auth/oidc/callback",
            "/runs",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        await store.SaveAsync(fresh, TestContext.Current.CancellationToken);

        var sweeper = server.Services.GetRequiredService<OidcStateSweeper>();
        await sweeper.SweepOnceAsync(TestContext.Current.CancellationToken);

        await using var verify = server.Services.CreateAsyncScope();
        var ids = await verify.ServiceProvider.GetRequiredService<IdentityDbContext>().OidcStates
            .AsNoTracking()
            .Select(static state => state.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.ShouldNotContain(expired.Id, "the expired row must be gone");
        ids.ShouldContain(fresh.Id, "the fresh row must survive");
    }
}
