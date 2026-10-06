using Comuki.Shared.Contracts.Verification;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF value converters for the verification axis (add-orchestra §3 —
/// Coda). One per persisted closed-set value (the verdict) plus the
/// value comparer for the jsonb evidence array. Wire form is the
/// lowercase value <see cref="GateVerdict.Value"/> carries ("pending"
/// / "passed" / "failed"); <see cref="GateVerdict.FromWire"/> collapses
/// any unknown wire value to <see cref="GateVerdict.Unspecified"/> on
/// read so a freshly-loaded row never throws on a value the catalogue
/// has not seen yet.
/// </summary>
internal static class VerificationSmartTypeConverters
{
    /// <summary>Bidirectional converter for <see cref="GateVerdict"/>.</summary>
    public static readonly ValueConverter<GateVerdict, string> GateVerdictToString =
        new(
            static verdict => verdict.Value,
            static wire => GateVerdict.FromWire(wire));

    /// <summary>
    /// Value comparer for the evidence-refs array — sequence-equal on
    /// read, aggregate hash on write, snapshot on track. EF's change
    /// tracker would otherwise mis-detect "changed" on reference-stable
    /// lists because the jsonb converter returns a different list
    /// instance on every materialization.
    /// </summary>
    public static readonly ValueComparer<IReadOnlyList<GateEvidenceRef>> GateEvidenceRefsComparer =
        new(
            equalsExpression: static (left, right) => ReferenceEquals(left, right)
                || (left != null && right != null && left.SequenceEqual(right)),
            hashCodeExpression: static refs => refs.Aggregate(
                0,
                static (accumulator, reference) => HashCode.Combine(
                    accumulator,
                    reference.Kind.Value,
                    reference.Uri.ToString(),
                    StringComparer.Ordinal)),
            snapshotExpression: static refs => refs.ToList());
}
