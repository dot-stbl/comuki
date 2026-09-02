using Comuki.Migrator.Sources;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Costs;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>: Costs migrations are authored
/// in Comuki.Modules.Costs.Infrastructure; this host supplies the
/// connection string (env <c>COMUKI_DB</c> or appsettings.json).
/// </summary>
public sealed class CostsDesignTimeFactory : IDesignTimeDbContextFactory<CostsDbContext>
{
    /// <inheritdoc />
    public CostsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<CostsDbContext>();
        CostsDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new CostsDbContext(builder.Options);
    }
}
