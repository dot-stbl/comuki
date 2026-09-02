using Comuki.Modules.Intake.Domain.Connections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Intake.Infrastructure.Persistence.Configurations;

/// <summary>
/// source_connections mapping: settings as jsonb, env-var names as
/// bounded strings, the webhook routing key unique.
/// </summary>
public sealed class SourceConnectionConfiguration : IEntityTypeConfiguration<SourceConnection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SourceConnection> builder)
    {
        builder.ToTable(IntakeTables.Connections);
        builder.HasKey(static connection => connection.Id);

        builder.Property(static connection => connection.Id)
            .HasColumnName("id")
            .HasConversion(IntakeIdConverters.ConnectionIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static connection => connection.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(IntakeIdConverters.ProjectIdToUuid);

        builder.Property(static connection => connection.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static connection => connection.Name)
            .HasColumnName("name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static connection => connection.SettingsJson)
            .HasColumnName("settings_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static connection => connection.SecretEnvRef)
            .HasColumnName("secret_env_ref")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(static connection => connection.WebhookKey)
            .HasColumnName("webhook_key")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static connection => connection.Enabled)
            .HasColumnName("enabled");

        builder.Property(static connection => connection.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static connection => connection.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static connection => connection.WebhookKey)
            .IsUnique()
            .HasDatabaseName("ux_source_connections_webhook_key");

        builder.HasIndex(static connection => connection.ProjectId)
            .HasDatabaseName("ix_source_connections_project");
    }
}
