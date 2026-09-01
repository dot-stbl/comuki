using Comuki.Modules.Chat.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Chat.Infrastructure.Persistence.Configurations;

/// <summary>chat_sessions mapping: uuid id, optional project scope, lifecycle stamps.</summary>
public sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {



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
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(static session => session.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(static session => session.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static session => session.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static session => session.SubjectId)
            .HasDatabaseName("ix_chat_sessions_subject_id");
    }
}
