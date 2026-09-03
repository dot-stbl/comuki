namespace Comuki.Modules.Chat.Infrastructure.Persistence;

/// <summary>
/// Physical Chat database — the Postgres schema name plus every table that
/// belongs to it. Single source every <c>IEntityTypeConfiguration</c> reads;
/// no magic strings in <c>builder.ToTable(...)</c>. The Voluta checkpoint
/// table is re-mapped from the package default (<c>voluta_checkpoints</c>)
/// onto the memory-contract name <c>chat_checkpoints</c>. The migration
/// history table lives at <c>chat.__ef_migrations_history</c> (per the EF
/// Core Postgres convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="ChatDbContext.ApplyOptions"/>.
/// </summary>
public static class ChatDatabase
{
    /// <summary>Postgres schema name. the namespace.</summary>
    public const string Schema = "chat";

    /// <summary>Sessions.</summary>
    public const string Sessions = "chat_sessions";

    /// <summary>Append-only transcript rows.</summary>
    public const string Messages = "chat_messages";

    /// <summary>Voluta graph checkpoints (jsonb state + pending interrupt).</summary>
    public const string Checkpoints = "chat_checkpoints";
}
