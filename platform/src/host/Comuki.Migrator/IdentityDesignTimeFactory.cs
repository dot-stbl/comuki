using Comuki.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>: Identity migrations are
/// authored in Comuki.Modules.Identity.Infrastructure, this host supplies
/// the connection string (env <c>COMUKI_DB</c> or appsettings.json —
/// never used to connect at design time, only to build the provider
/// model).
/// </summary>
public sealed class IdentityDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <inheritdoc />
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new IdentityDbContext(builder.Options);
    }
}
