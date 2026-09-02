using Comuki.Modules.Memory.Domain.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;

/// <summary>chat_checkpoints mapping: session_id PK, graph_state jsonb, updated_at.</summary>
public sealed class ChatCheckpointConfiguration : IEntityTypeConfiguration<ChatCheckpoint>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ChatCheckpoint> builder)
    {
        builder.ToTable(MemoryTables.ChatCheckpoints);
        builder.HasKey(static checkpoint => checkpoint.SessionId);

        builder.Property(static checkpoint => checkpoint.SessionId)
            .HasColumnName("session_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static checkpoint => checkpoint.GraphState)
            .HasColumnName("graph_state")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static checkpoint => checkpoint.UpdatedAt)
            .HasColumnName("updated_at");
    }
}
