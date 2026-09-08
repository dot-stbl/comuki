using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Stores;

/// <summary>EF implementation of <see cref="IOidcLinkStore"/>.</summary>
/// <param name="db"></param>
public sealed class OidcLinkStore(IdentityDbContext db) : IOidcLinkStore
{
    /// <inheritdoc />
    public async Task<OidcLink?> FindAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = provider.Trim().ToLowerInvariant();

        return await db.OidcLinks.SingleOrDefaultAsync(
            link => link.Provider == normalizedProvider && link.Subject == subject,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OidcLink?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();

        return await db.OidcLinks
            .Join(db.Users, link => link.UserId, user => user.Id, (link, user) => new { link, user })
            .Where(joined => joined.user.Email == normalized)
            .Select(joined => joined.link)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(OidcLink link, CancellationToken cancellationToken = default)
    {
        if (db.Entry(link).State == EntityState.Detached)
        {
            db.OidcLinks.Add(link);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
