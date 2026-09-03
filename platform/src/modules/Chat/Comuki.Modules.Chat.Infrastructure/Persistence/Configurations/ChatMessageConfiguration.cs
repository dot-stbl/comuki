using Comuki.Modules.Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Chat.Infrastructure.Persistence.Configurations;

/// <summary>chat_messages mapping: append-only transcript rows keyed by uuidv7, session index for the history window.</summary>
public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    /// <summary>Maximum message length (mirror of the application validator bound).</summary>
    public const int MaxContentLength = 8000;

    /// <summary>Maximum tool name length.</summary>
    public const int MaxToolNameLength = 64;

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

        builder.Property(static message => message.Role)
            .HasColumnName("role")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(static message => message.Content)
            .HasColumnName("content")
            .HasMaxLength(MaxContentLength)
            .IsRequired();

        builder.Property(static message => message.ToolName)
            .HasColumnName("tool_name")
            .HasMaxLength(MaxToolNameLength);

        builder.Property(static message => message.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(static message => message.SessionId)
            .HasDatabaseName("ix_chat_messages_session_id");
    }
}
