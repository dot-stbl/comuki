using Comuki.Migrator.Sources;
using Comuki.Modules.Verify.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Verify;

/// <summary>
/// Design-time factory for <see cref="VerifyDbContext"/>: reads the same
/// connection-string source as the Migrator itself so <c>dotnet ef</c>
/// can build the model without booting the host.
/// </summary>
public sealed class VerifyDesignTimeFactory() : IDesignTimeDbContextFactory<VerifyDbContext>
{
    /// <inheritdoc />
    public VerifyDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<VerifyDbContext>();
        VerifyDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new VerifyDbContext(builder.Options);
    }
}
