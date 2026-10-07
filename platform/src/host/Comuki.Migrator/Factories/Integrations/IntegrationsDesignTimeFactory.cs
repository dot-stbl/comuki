using Comuki.Migrator.Sources;
using Comuki.Modules.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Integrations;

/// <summary>
/// Design-time factory for <see cref="IntegrationsDbContext"/>: reads the same
/// connection-string source as the Migrator itself so
/// <c>dotnet ef</c> can build the model without booting the host.
/// </summary>
public sealed class IntegrationsDesignTimeFactory() : IDesignTimeDbContextFactory<IntegrationsDbContext>
{
    /// <inheritdoc />
    public IntegrationsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IntegrationsDbContext>();
        IntegrationsDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new IntegrationsDbContext(builder.Options);
    }
}
