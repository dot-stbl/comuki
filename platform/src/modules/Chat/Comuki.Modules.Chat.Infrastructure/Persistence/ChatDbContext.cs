using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Modules.Chat.Domain.Sessions;
using Comuki.Modules.Chat.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Voluta.Checkpoints.EntityFrameworkCore;

namespace Comuki.Modules.Chat.Infrastructure.Persistence;

/// <summary>
/// EF model for the Chat schema: chat_sessions / chat_messages plus the
/// Voluta checkpoint table (re-mapped onto <c>chat_checkpoints</c> per the
/// memory contract — the package default is <c>voluta_checkpoints</c>).
/// The context implements <see cref="IVolutaCheckpointDbContext"/> so the
/// EF Core checkpointer rides the same Npgsql connection and the module's
/// own migrations history. Snake_case naming is applied by the shared
/// options recipe (<see cref="ApplyOptions"/>) via
/// <c>UseSnakeCaseNamingConvention</c>.
/// </summary>
/// <param name="options"></param>
public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options)
    : DbContext(options), IVolutaCheckpointDbContext
{
    /// <summary>Sessions — aggregate roots.</summary>
    public DbSet<ChatSession> Sessions => Set<ChatSession>();

    /// <summary>Append-only transcript.</summary>
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    /// <summary>Voluta graph checkpoints (thread = session id).</summary>
    public DbSet<CheckpointRecord> Checkpoints => Set<CheckpointRecord>();

    /// <summary>
    /// Single options recipe (Npgsql + snake_case + private history table)
    /// used by the DI extension, the design-time factory and the Migrator —
    /// one place, no drift.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionString"></param>
    public static void ApplyOptions(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, static npgsql => npgsql.MigrationsHistoryTable(ChatTables.MigrationsHistory))
            .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new ChatSessionConfiguration())
            .ApplyConfiguration(new ChatMessageConfiguration())
            .ApplyVolutaCheckpointModel();

        // memory-contract table name instead of the package default voluta_checkpoints
        modelBuilder.Entity<CheckpointRecord>()
            .ToTable(ChatTables.Checkpoints);

        base.OnModelCreating(modelBuilder);
    }
}
