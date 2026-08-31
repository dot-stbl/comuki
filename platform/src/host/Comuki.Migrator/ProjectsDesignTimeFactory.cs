using Comuki.Modules.Projects.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>: Projects migrations are
/// authored in Comuki.Modules.Projects.Infrastructure, this host supplies
/// the connection string (env <c>COMUKI_DB</c> or appsettings.json —
/// never used to connect at design time, only to build the provider
/// model).
/// </summary>
public sealed class ProjectsDesignTimeFactory : IDesignTimeDbContextFactory<ProjectsDbContext>
{
    /// <inheritdoc />
    public ProjectsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ProjectsDbContext>();
        ProjectsDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new ProjectsDbContext(builder.Options);
    }
}
