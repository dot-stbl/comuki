using Comuki.Modules.Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Chat.Infrastructure.Persistence.Configurations;

/// <summary>chat_messages mapping: append-only transcript rows keyed by uuidv7, session index for the history window.</summary>
public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    /// <summary>Maximum message length (mirror of the application validator bound).</summary>
    public const int MaxContentLength = ChatMessage.MaxContentLength;

    /// <summary>Maximum tool name length.</summary>
    public const int MaxToolNameLength = 64;

    /// <summary>Maximum role name length — the column stores the enum member name.</summary>
    public const int MaxRoleLength = 16;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable(ChatDatabase.Messages, ChatDatabase.Schema);
        builder.HasKey(static message => message.Id);

        builder.Property(static message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static message => message.SessionId)
            .HasColumnName("session_id")
            .HasConversion(ChatIdConverters.ChatSessionIdToUuid)
            .IsRequired();

        // String storage, not the ordinal: the role reads in psql and a
        // reordered enum cannot silently relabel history. Chat was the
        // last module persisting an enum as int.
        builder.Property(static message => message.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(MaxRoleLength)
            .IsRequired();

        builder.Property(static message => message.Content)
            .HasColumnName("content")
            .HasMaxLength(MaxContentLength)
            .IsRequired();

        builder.Property(static message => message.ToolName)
            .HasColumnName("tool_name")
            .HasMaxLength(MaxToolNameLength);

        // The rich shape of the row. Both are optional: a row that
        // predates parts keeps only its flat content projection, and the
        // column type bounds the payload (no HasMaxLength on jsonb).
        builder.Property(static message => message.PartsJson)
            .HasColumnName("parts")
            .HasColumnType("jsonb");

        builder.Property(static message => message.MetaJson)
            .HasColumnName("meta")
            .HasColumnType("jsonb");

        builder.Property(static message => message.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(static message => message.SessionId)
            .HasDatabaseName("ix_chat_messages_session_id");
    }
}
