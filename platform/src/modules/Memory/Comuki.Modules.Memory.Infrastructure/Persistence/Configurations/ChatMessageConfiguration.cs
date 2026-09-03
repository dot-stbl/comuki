using Comuki.Modules.Memory.Domain.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;

/// <summary>chat_messages mapping: uuid id, session+role+body, created_at.</summary>
public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable(MemoryDatabase.ChatMessages, MemoryDatabase.Schema);
        builder.HasKey(static message => message.Id);

        builder.Property(static message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static message => message.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static message => message.Role)
            .HasColumnName("role")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static message => message.Content)
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(static message => message.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(static message => message.SessionId)
            .HasDatabaseName("ix_chat_messages_session_id");
    }
}
