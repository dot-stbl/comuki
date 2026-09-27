using Comuki.Migrator.Sources;
using Comuki.Modules.Repositories.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Repositories;

/// <summary>
/// Design-time factory for <see cref="RepositoriesDbContext"/>: reads
/// the same connection-string source as the Migrator itself so
/// <c>dotnet ef</c> can build the model without booting the host.
/// </summary>
public sealed class RepositoriesDesignTimeFactory() : IDesignTimeDbContextFactory<RepositoriesDbContext>
{
    /// <inheritdoc />
    public RepositoriesDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<RepositoriesDbContext>();
        RepositoriesDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new RepositoriesDbContext(builder.Options);
    }
}
