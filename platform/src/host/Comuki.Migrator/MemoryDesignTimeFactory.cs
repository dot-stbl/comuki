using Comuki.Modules.Memory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>: Memory migrations are
/// authored in Comuki.Modules.Memory.Infrastructure, this host supplies
/// the connection string (env <c>COMUKI_DB</c> or appsettings.json —
/// never used to connect at design time, only to build the provider
/// model).
/// </summary>
public sealed class MemoryDesignTimeFactory : IDesignTimeDbContextFactory<MemoryDbContext>
{
    /// <inheritdoc />
    public MemoryDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MemoryDbContext>();
        MemoryDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new MemoryDbContext(builder.Options);
    }
}
