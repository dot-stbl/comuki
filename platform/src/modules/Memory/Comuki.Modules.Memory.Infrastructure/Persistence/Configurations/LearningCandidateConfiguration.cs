using Comuki.Modules.Memory.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;

/// <summary>
/// learning_candidates mapping: uuid id, project scope, topic, observation,
/// proposed rule, source ref, repeat counter, status key, decision reason,
/// decided_at.
/// </summary>
public sealed class LearningCandidateConfiguration : IEntityTypeConfiguration<LearningCandidate>
{
    /// <summary>Upper bound of <see cref="LearningCandidate.Topic"/> — the memory_facts.topic_key column limit minus the rule-topic prefix.</summary>
    public const int TopicMaxLength = 200;

    /// <summary>Upper bound of <see cref="LearningCandidate.Observation"/>.</summary>
    public const int ObservationMaxLength = 2000;

    /// <summary>Upper bound of <see cref="LearningCandidate.ProposedRule"/>.</summary>
    public const int ProposedRuleMaxLength = 2000;

    /// <summary>Upper bound of <see cref="LearningCandidate.DecisionReason"/>.</summary>
    public const int DecisionReasonMaxLength = 1000;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LearningCandidate> builder)
    {
        builder.ToTable(MemoryDatabase.LearningCandidates, MemoryDatabase.Schema);
        builder.HasKey(static candidate => candidate.Id);

        builder.Property(static candidate => candidate.Id)
            .HasColumnName("id")
            .HasConversion(MemoryIdConverters.LearningCandidateIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static candidate => candidate.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        builder.Property(static candidate => candidate.Topic)
            .HasColumnName("topic")
            .HasMaxLength(TopicMaxLength)
            .IsRequired();

        builder.Property(static candidate => candidate.Observation)
            .HasColumnName("observation")
            .HasMaxLength(ObservationMaxLength)
            .IsRequired();

        builder.Property(static candidate => candidate.ProposedRule)
            .HasColumnName("proposed_rule")
            .HasMaxLength(ProposedRuleMaxLength)
            .IsRequired();

        builder.Property(static candidate => candidate.SourceRef)
            .HasColumnName("source_ref")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static candidate => candidate.RepeatCount)
            .HasColumnName("repeat_count");

        builder.Property(static candidate => candidate.Status)
            .HasColumnName("status")
            .HasConversion(MemoryKeyConverters.LearningStatusToKey)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static candidate => candidate.DecisionReason)
            .HasColumnName("decision_reason")
            .HasMaxLength(DecisionReasonMaxLength);

        builder.Property(static candidate => candidate.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static candidate => candidate.DecidedAt)
            .HasColumnName("decided_at");

        builder.HasIndex(static candidate => candidate.Status)
            .HasDatabaseName("ix_learning_candidates_status");
    }
}
