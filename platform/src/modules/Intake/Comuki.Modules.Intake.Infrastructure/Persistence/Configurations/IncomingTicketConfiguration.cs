using Comuki.Modules.Intake.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Intake.Infrastructure.Persistence.Configurations;

/// <summary>
/// intake_tickets mapping: text[] labels, string enums, and the
/// one-live-run-per-issue lock — a partial unique index over
/// (project_id, provider, external_id) restricted to the active
/// statuses. EF's HasConversion&lt;string&gt; stores PascalCase enum
/// names, so the filter predicates must match them.
/// </summary>
public sealed class IncomingTicketConfiguration : IEntityTypeConfiguration<IncomingTicket>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IncomingTicket> builder)
    {
        builder.ToTable(IntakeDatabase.Tickets, IntakeDatabase.Schema);
        builder.HasKey(static ticket => ticket.Id);

        builder.Property(static ticket => ticket.Id)
            .HasColumnName("id")
            .HasConversion(IntakeIdConverters.TicketIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static ticket => ticket.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(IntakeIdConverters.ProjectIdToUuid);

        builder.Property(static ticket => ticket.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static ticket => ticket.ExternalId)
            .HasColumnName("external_id")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static ticket => ticket.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static ticket => ticket.Body)
            .HasColumnName("body")
            .HasMaxLength(32768)
            .IsRequired();

        builder.Property(static ticket => ticket.Author)
            .HasColumnName("author")
            .HasMaxLength(256);

        builder.Property(static ticket => ticket.Url)
            .HasColumnName("url")
            .HasMaxLength(2048);

        builder.Property(static ticket => ticket.ProjectKey)
            .HasColumnName("project_key")
            .HasMaxLength(256);

        builder.Property(static ticket => ticket.Labels)
            .HasColumnName("labels")
            .HasColumnType("text[]");

        builder.Property(static ticket => ticket.ConnectionId)
            .HasColumnName("connection_id")
            .HasConversion(IntakeIdConverters.ConnectionIdToUuid);

        builder.Property(static ticket => ticket.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static ticket => ticket.RunId)
            .HasColumnName("run_id")
            .HasConversion(IntakeIdConverters.RunIdToUuid);

        builder.Property(static ticket => ticket.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static ticket => ticket.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static ticket => ticket.UpdatedAt)
            .HasColumnName("updated_at");

        // Lock #2 — one active ticket (hence one live run) per issue.
        builder.HasIndex(static ticket => new { ticket.ProjectId, ticket.Provider, ticket.ExternalId })
            .IsUnique()
            .HasDatabaseName("ux_intake_tickets_active")
            .HasFilter("status IN ('Pending', 'Claimed')");

        builder.HasIndex(static ticket => ticket.CreatedAt)
            .HasDatabaseName("ix_intake_tickets_pending")
            .HasFilter("status = 'Pending'");

        builder.HasIndex(static ticket => ticket.UpdatedAt)
            .HasDatabaseName("ix_intake_tickets_claimed")
            .HasFilter("status = 'Claimed'");
    }
}
