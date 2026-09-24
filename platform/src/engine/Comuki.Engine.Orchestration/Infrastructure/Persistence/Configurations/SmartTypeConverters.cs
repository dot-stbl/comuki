using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Domain.Runs;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF value converters for the engine's domain smart-types — one per
/// persisted closed-set value (statuses, trust class, conflict
/// resolution). The wire form is the PascalCase text the prior
/// enum-to-string converter produced (and which the DB schema, raw-SQL
/// literals and partial-index predicates all already match), so this
/// slice preserves existing rows and predicates byte-for-byte. Read-side
/// failures (unknown wire values, empty strings) collapse to the
/// smart-type's default — the column default of
/// <see cref="RunTrustClass.Supervised"/> applies here too, so a
/// freshly-loaded row never throws.
/// </summary>
internal static class SmartTypeConverters
{
    /// <summary>Bidirectional converter for <see cref="RunStatus"/>.</summary>
    public static readonly ValueConverter<RunStatus, string> RunStatusToString =
        new(
            static runStatus => runStatus.Value,
            static wire => RunStatus.FromWire(wire));

    /// <summary>Bidirectional converter for <see cref="WorkItemStatus"/>.</summary>
    public static readonly ValueConverter<WorkItemStatus, string> WorkItemStatusToString =
        new(
            static workItemStatus => workItemStatus.Value,
            static wire => WorkItemStatus.FromWire(wire));

    /// <summary>Bidirectional converter for <see cref="MergeQueueStatus"/>.</summary>
    public static readonly ValueConverter<MergeQueueStatus, string> MergeQueueStatusToString =
        new(
            static mergeQueueStatus => mergeQueueStatus.Value,
            static wire => MergeQueueStatus.FromWire(wire));

    /// <summary>Bidirectional converter for <see cref="MergeBatchStatus"/>.</summary>
    public static readonly ValueConverter<MergeBatchStatus, string> MergeBatchStatusToString =
        new(
            static mergeBatchStatus => mergeBatchStatus.Value,
            static wire => MergeBatchStatus.FromWire(wire));

    /// <summary>Bidirectional converter for <see cref="ConflictResolution"/>.</summary>
    public static readonly ValueConverter<ConflictResolution, string> ConflictResolutionToString =
        new(
            static conflictResolution => conflictResolution.Value,
            static wire => ConflictResolution.FromWire(wire));

    /// <summary>Bidirectional converter for <see cref="RunTrustClass"/>.</summary>
    public static readonly ValueConverter<RunTrustClass, string> RunTrustClassToString =
        new(
            static trustClass => trustClass.Value,
            static wire => RunTrustClass.FromWire(wire));
}
