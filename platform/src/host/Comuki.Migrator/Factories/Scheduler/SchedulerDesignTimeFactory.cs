using Comuki.Migrator.Sources;
using Comuki.Modules.Scheduler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Scheduler;

/// <summary>
/// Design-time factory for <see cref="SchedulerDbContext"/>: reads the
/// same connection-string source as the Migrator itself so
/// <c>dotnet ef</c> can build the model without booting the host.
/// </summary>
public sealed class SchedulerDesignTimeFactory() : IDesignTimeDbContextFactory<SchedulerDbContext>
{
    /// <inheritdoc />
    public SchedulerDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SchedulerDbContext>();
        SchedulerDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new SchedulerDbContext(builder.Options);
    }
}
