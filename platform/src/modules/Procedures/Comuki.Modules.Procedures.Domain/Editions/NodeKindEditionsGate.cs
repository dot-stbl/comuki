using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Exceptions;
using Comuki.Modules.Procedures.Domain.Loading;

namespace Comuki.Modules.Procedures.Domain.Editions;

/// <summary>
/// Standalone gate the compile gate (task 2.3) calls before publishing
/// a catalog. Refuses publication when the effective edition does not
/// grant a kind's <see cref="Kinds.NodeKindDescriptor.EditionsFeatureKey"/>.
/// Refusal happens at compile time — never mid-run — so a pinned version
/// stays executable under the edition that compiled it even after a
/// downgrade (spec scenario: "Paid kind inside an active pin after
/// downgrade").
/// 
/// <para>
/// Stateless: the gate has no fields and reads everything from its
/// arguments. The class is <c>static</c> — calling it does not require
/// DI wiring, and the compile gate (task 2.3) invokes the method
/// directly with the resolved <see cref="GrantedFeatureKeys"/>.
/// </para>
/// 
/// <para>
/// This gate is structurally separate from
/// <see cref="Validation.CatalogValidator"/> and
/// <see cref="NodeKindDescriptorDocumentParser"/>: the reader parses descriptors,
/// the validator enforces shape and bumps versions, this gate enforces
/// editions. Composing them is the compile gate's job (task 2.3).
/// </para>
/// </summary>
public static class NodeKindEditionsGate
{
    /// <summary>
    /// Validates every descriptor against the effective edition's
    /// granted feature keys. Kinds without an
    /// <see cref="Kinds.NodeKindDescriptor.EditionsFeatureKey"/> always pass
    /// (no opt-in — no check). The first descriptor whose feature key is
    /// not granted fails fast and the gate throws
    /// <see cref="ProcedureNodeKindsDomainException"/> with
    /// <see cref="ProcedureNodeKindsDomainException.FeatureKeyNotGranted"/>,
    /// naming both the kind and the missing key so the operator knows
    /// which line of the catalog to fix (spec scenario: "Two-approval gate
    /// on community").
    /// </summary>
    /// <param name="entries">The proposed catalog entries.</param>
    /// <param name="granted">The effective edition's granted feature keys.</param>
    public static void ValidateEntries(
        IReadOnlyList<NodeKindCatalogEntry> entries,
        GrantedFeatureKeys granted)
    {
        foreach (var entry in entries)
        {
            var featureKey = entry.Descriptor.EditionsFeatureKey;
            if (string.IsNullOrEmpty(featureKey))
            {
                continue;
            }

            if (granted.IsGranted(featureKey))
            {
                continue;
            }

            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.FeatureKeyNotGranted,
                $"Kind '{entry.Key}' requires editions feature '{featureKey}' which is not granted by the effective edition; refused at compile time (spec: a kind descriptor MAY carry an editions feature key — the compile gate refuses when the effective edition does not grant it).");
        }
    }
}
