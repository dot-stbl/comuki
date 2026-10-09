using Comuki.Migrator.Sources;
using Comuki.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Work;

/// <summary>
/// Design-time factory for <see cref="WorkDbContext"/>: reads the
/// same connection-string source as the Migrator itself so
/// <c>dotnet ef</c> can build the model without booting the host.
/// </summary>
public sealed class WorkDesignTimeFactory() : IDesignTimeDbContextFactory<WorkDbContext>
{
    /// <inheritdoc />
    public WorkDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<WorkDbContext>();
        WorkDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new WorkDbContext(builder.Options);
    }
}
