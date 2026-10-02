using Comuki.Migrator.Sources;
using Comuki.Modules.Procedures.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Procedures;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>: Procedures migrations are
/// authored in Comuki.Modules.Procedures.Infrastructure; this host
/// supplies the connection string (env <c>COMUKI_DB</c> or config.toml).
/// </summary>
public sealed class ProceduresDesignTimeFactory() : IDesignTimeDbContextFactory<ProceduresDbContext>
{
    /// <inheritdoc />
    public ProceduresDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ProceduresDbContext>();
        ProceduresDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new ProceduresDbContext(builder.Options);
    }
}
