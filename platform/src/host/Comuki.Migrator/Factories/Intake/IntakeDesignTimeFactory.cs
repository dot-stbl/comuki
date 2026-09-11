using Comuki.Migrator.Sources;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Intake;

/// <summary>
/// Design-time factory for <see cref="IntakeDbContext"/>: reads the same
/// connection-string source as the Migrator itself so
/// <c>dotnet ef</c> can build the model without booting the host.
/// </summary>
public sealed class IntakeDesignTimeFactory() : IDesignTimeDbContextFactory<IntakeDbContext>
{
    /// <inheritdoc />
    public IntakeDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IntakeDbContext>();
        IntakeDbContext.ApplyOptions(builder, ConnectionStringSource.ResolveOrThrow());
        return new IntakeDbContext(builder.Options);
    }
}
