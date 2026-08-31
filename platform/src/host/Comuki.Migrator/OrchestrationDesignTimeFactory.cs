using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>: migrations are authored in
/// Comuki.Engine.Orchestration, this host supplies the connection string
/// (env <c>COMUKI_DB</c> or appsettings.json — never used to connect at
/// design time, only to build the provider model).
/// </summary>
public sealed class OrchestrationDesignTimeFactory : IDesignTimeDbContextFactory<OrchestrationDbContext>
{
    /// <inheritdoc />
    public OrchestrationDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new OrchestrationDbContext(builder.Options);
    }
}
