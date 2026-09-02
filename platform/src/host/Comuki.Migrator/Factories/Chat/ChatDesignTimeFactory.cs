using Comuki.Migrator.Sources;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comuki.Migrator.Factories.Chat;

/// <summary>
/// Design-time factory for <see cref="ChatDbContext"/>: reads the same
/// connection-string source as the Migrator itself so
/// <c>dotnet ef</c> can build the model without booting the host.
/// </summary>
public sealed class ChatDesignTimeFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    /// <inheritdoc />
    public ChatDbContext CreateDbContext(string[] args)
    {
        var connectionString = ConnectionStringSource.TryResolve(out _)
            ?? "Host=localhost;Database=comuki;Username=postgres;Password=postgres";

        var builder = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContext.ApplyOptions(builder, connectionString);
        return new ChatDbContext(builder.Options);
    }
}
