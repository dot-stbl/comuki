using Comuki.Migrator.Sources;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Knowledge;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>: Knowledge migrations are
/// authored in Comuki.Modules.Knowledge.Infrastructure, this host supplies
/// the connection string (env <c>COMUKI_DB</c> or appsettings.json —
/// never used to connect at design time, only to build the provider
/// model).
/// </summary>
public sealed class KnowledgeDesignTimeFactory : IDesignTimeDbContextFactory<KnowledgeDbContext>
{
    /// <inheritdoc />
    public KnowledgeDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<KnowledgeDbContext>();
        KnowledgeDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new KnowledgeDbContext(builder.Options);
    }
}
