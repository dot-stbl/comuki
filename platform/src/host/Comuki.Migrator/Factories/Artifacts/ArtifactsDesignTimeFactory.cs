using Comuki.Migrator.Sources;
using Comuki.Modules.Artifacts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Artifacts;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>: Artifacts migrations are
/// authored in <c>Comuki.Modules.Artifacts.Infrastructure</c>, this
/// host supplies the connection string (env <c>COMUKI_DB</c> or
/// config.toml — never used to connect at design time, only to
/// build the provider model).
/// </summary>
public sealed class ArtifactsDesignTimeFactory : IDesignTimeDbContextFactory<ArtifactsDbContext>
{
    /// <inheritdoc />
    public ArtifactsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ArtifactsDbContext>();
        ArtifactsDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new ArtifactsDbContext(builder.Options);
    }
}
