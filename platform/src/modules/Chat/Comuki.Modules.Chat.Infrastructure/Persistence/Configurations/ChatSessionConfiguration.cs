using Comuki.Modules.Chat.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Chat.Infrastructure.Persistence.Configurations;

/// <summary>chat_sessions mapping: uuid id, optional project scope, lifecycle stamps.</summary>
public sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    /// <summary>Maximum title length.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Maximum status name length — the column stores the enum member name.</summary>
    public const int MaxStatusLength = 16;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable(ChatDatabase.Sessions, ChatDatabase.Schema);
        builder.HasKey(static session => session.Id);

        builder.Property(static session => session.Id)
            .HasColumnName("id")
            .HasConversion(ChatIdConverters.ChatSessionIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static session => session.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(ChatIdConverters.ProjectIdToUuid);

        builder.Property(static session => session.SubjectId)
            .HasColumnName("subject_id")
            .IsRequired();

        builder.Property(static session => session.Title)
            .HasColumnName("title")
            .HasMaxLength(MaxTitleLength)
            .IsRequired();

        // String storage, not the ordinal — same reason as chat_messages.role.
        builder.Property(static session => session.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(MaxStatusLength)
            .IsRequired();

        builder.Property(static session => session.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static session => session.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static session => session.SubjectId)
            .HasDatabaseName("ix_chat_sessions_subject_id");
    }
}
