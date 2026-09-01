namespace Comuki.Modules.Chat.Infrastructure.Persistence;

/// <summary>
/// Physical Chat table names — the single source every EF configuration
/// reads; no magic strings in <c>IEntityTypeConfiguration</c>. The Voluta
/// checkpoint table is re-mapped from the package default
/// (<c>voluta_checkpoints</c>) onto the memory-contract name
/// <c>chat_checkpoints</c>.
/// </summary>
public static class ChatTables
{
    /// <summary>Sessions.</summary>
    public const string Sessions = "chat_sessions";

    /// <summary>Append-only transcript rows.</summary>
    public const string Messages = "chat_messages";

    /// <summary>Voluta graph checkpoints (jsonb state + pending interrupt).</summary>
    public const string Checkpoints = "chat_checkpoints";

    /// <summary>Module-private EF migrations history table.</summary>
    public const string MigrationsHistory = "__comuki_chat";
}
