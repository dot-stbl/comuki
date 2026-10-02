using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Exceptions;
using Comuki.Modules.Procedures.Domain.Kinds;
using Comuki.Modules.Procedures.Domain.Kinds.Types;
using Comuki.Modules.Procedures.Domain.Validation.Ports;

namespace Comuki.Modules.Procedures.Domain.Validation.Helpers;

/// <summary>
/// Pure static helpers the <see cref="CatalogValidator"/> orchestrates:
/// schema validation, diff computation, version bumping, and retraction
/// refusal. Extracted from the validator per the no-private-methods rule —
/// each helper is a pure function over its arguments and testable in
/// isolation from the DI-bound orchestrator.
/// </summary>
public static class CatalogValidation
{
    /// <summary>
    /// Per-descriptor schema check: non-empty key/title/description,
    /// non-blank port names, no duplicate ports inside a descriptor, and
    /// no duplicate keys across the catalog. Defensive — the reader
    /// enforces most of this, but the validator re-checks so callers that
    /// construct a draft in code (not from a file) get the same guarantee.
    /// </summary>
    public static void ValidateSchema(IReadOnlyList<NodeKindCatalogEntry> entries)
    {
        if (entries is null || entries.Count == 0)
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.SchemaViolation,
                "Catalog draft must contain at least one kind descriptor.");
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            RefuseBlankKey(entry.Key);
            RefuseDuplicateKey(seenKeys, entry.Key);
            RefuseBlankDescriptorFields(entry);
            RefuseBlankOrDuplicatePorts(entry);
            RefuseBlankOrDuplicateEvidence(entry);
        }
    }

    /// <summary>
    /// Compares the draft to the previous catalog by key. Added / altered /
    /// retracted are mutually exclusive per key. <c>Altered</c> requires at
    /// least one wire-contract field to differ; editorial fields
    /// (Title/Description/RiskClass) are ignored — the spec scopes bumps to
    /// ports, parameters, and evidence (plus owner surface / idempotency /
    /// approval floor because they are part of the dispatch contract).
    /// </summary>
    public static NodeKindCatalogDiff ComputeDiff(
        IReadOnlyList<NodeKindCatalogEntry> draftEntries,
        NodeKindCatalog? previous)
    {
        if (previous is null)
        {
            var addedKeys = draftEntries.Select(static entry => entry.Key).ToList();
            addedKeys.Sort(StringComparer.Ordinal);
            return new NodeKindCatalogDiff(addedKeys, [], []);
        }

        var previousByKey = previous.Entries.ToDictionary(
            static entry => entry.Key, static entry => entry.Descriptor, StringComparer.Ordinal);
        var draftByKey = draftEntries.ToDictionary(
            static entry => entry.Key, static entry => entry.Descriptor, StringComparer.Ordinal);

        var added = new List<string>();
        var altered = new List<string>();
        var retracted = new List<string>();

        foreach (var pair in draftByKey)
        {
            if (!previousByKey.ContainsKey(pair.Key))
            {
                added.Add(pair.Key);
                continue;
            }

            if (HasContractDifference(previousByKey[pair.Key], pair.Value))
            {
                altered.Add(pair.Key);
            }
        }

        foreach (var pair in previousByKey)
        {
            if (!draftByKey.ContainsKey(pair.Key))
            {
                retracted.Add(pair.Key);
            }
        }

        added.Sort(StringComparer.Ordinal);
        altered.Sort(StringComparer.Ordinal);
        retracted.Sort(StringComparer.Ordinal);
        return new NodeKindCatalogDiff(added, altered, retracted);
    }

    /// <summary>
    /// For each retracted key, asks the usage lookup whether any published
    /// procedure still references it. Non-empty → refusal with the
    /// referencing procedure names.
    /// </summary>
    public static async Task RefuseRetractionsInUseAsync(
        IProcedureKindUsageLookup usageLookup,
        IReadOnlyList<string> retractedKeys,
        CancellationToken cancellationToken)
    {
        foreach (var key in retractedKeys)
        {
            var references = await usageLookup.FindReferencesAsync(key, cancellationToken);
            if (references.Count == 0)
            {
                continue;
            }

            var names = string.Join(", ", references.Select(static reference => reference.ProcedureName));
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.RetractionBlocked,
                $"Kind '{key}' is referenced by published procedures: {names}; retraction refused.");
        }
    }

    /// <summary>
    /// Computes the next catalog version per the spec: additive-only
    /// changes bump the minor; alterations and retractions bump the major —
    /// both are contract-changing events a pinned run must not silently
    /// inherit. Unchanged drafts return the previous version unchanged.
    /// </summary>
    public static string ComputeNextVersion(string previousVersion, NodeKindCatalogDiff diff)
    {
        if (!diff.HasChanges)
        {
            return previousVersion;
        }

        var version = ParseVersion(previousVersion);
        var isBreaking = diff.AlteredKeys.Count > 0 || diff.RetractedKeys.Count > 0;
        return isBreaking
            ? $"{version.Major + 1}.0"
            : $"{version.Major}.{version.Minor + 1}";
    }

    /// <summary>
    /// Returns true when the wire-contract fields of the two descriptors
    /// differ. Editorial fields are ignored — the spec scopes version
    /// bumps to ports/parameters/evidence; owner surface, idempotency,
    /// and approval floor are added because the compile gate reads them.
    /// </summary>
    internal static bool HasContractDifference(NodeKindDescriptor left, NodeKindDescriptor right)
    {
        return left.OwnerSurface != right.OwnerSurface
            || left.Idempotency != right.Idempotency
            || left.ApprovalFloor != right.ApprovalFloor
            || !string.Equals(left.ParameterSchema, right.ParameterSchema, StringComparison.Ordinal)
            || !PortsEqual(left.OutcomePorts, right.OutcomePorts)
            || !StringSetEqual(left.EvidenceRequirements, right.EvidenceRequirements);
    }

    /// <summary>
    /// Parses a <c>major.minor</c> version string. A malformed version is
    /// a contract violation, surfaced as a refusal.
    /// </summary>
    internal static CatalogVersion ParseVersion(string version)
    {
        return CatalogVersionParser.TryParse(version)
            ?? throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.SchemaViolation,
                $"Catalog version '{version}' is not in major.minor format.");
    }

    /// <summary>Refuses a descriptor key that is empty or whitespace.</summary>
    /// <param name="key">The descriptor file-stem key.</param>
    public static void RefuseBlankKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.SchemaViolation,
                "Descriptor key must be non-empty.");
        }
    }

    /// <summary>Refuses a descriptor key already seen in this draft; <paramref name="seenKeys"/> absorbs the key on success.</summary>
    /// <param name="seenKeys">Keys accepted so far in this draft.</param>
    /// <param name="key">The descriptor key being accepted.</param>
    public static void RefuseDuplicateKey(HashSet<string> seenKeys, string key)
    {
        if (!seenKeys.Add(key))
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.SchemaViolation,
                $"Catalog draft contains a duplicate descriptor key '{key}'.");
        }
    }

    /// <summary>Refuses a descriptor whose title, description, or owner surface is blank or Unspecified.</summary>
    /// <param name="entry">The draft entry being validated.</param>
    public static void RefuseBlankDescriptorFields(NodeKindCatalogEntry entry)
    {
        var descriptor = entry.Descriptor;
        if (string.IsNullOrWhiteSpace(descriptor.Title))
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.SchemaViolation,
                $"Descriptor '{entry.Key}' has an empty title.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.Description))
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.SchemaViolation,
                $"Descriptor '{entry.Key}' has an empty description.");
        }

        if (descriptor.OwnerSurface == NodeKindOwnerSurface.Unspecified)
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.OwnerSurfaceMissing,
                $"Descriptor '{entry.Key}' owner surface is Unspecified — every descriptor must declare one of WorkItem|Decision|BrokerOperation|BrainOperation.");
        }
    }

    /// <summary>Refuses a descriptor whose outcome ports are blank or name-collide within the descriptor.</summary>
    /// <param name="entry">The draft entry being validated.</param>
    public static void RefuseBlankOrDuplicatePorts(NodeKindCatalogEntry entry)
    {
        var portNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var port in entry.Descriptor.OutcomePorts)
        {
            if (string.IsNullOrWhiteSpace(port.Name) || port.Name == "<unspecified>")
            {
                throw new ProcedureNodeKindsDomainException(
                    ProcedureNodeKindsDomainException.SchemaViolation,
                    $"Descriptor '{entry.Key}' declares a blank outcome port.");
            }

            if (!portNames.Add(port.Name))
            {
                throw new ProcedureNodeKindsDomainException(
                    ProcedureNodeKindsDomainException.SchemaViolation,
                    $"Descriptor '{entry.Key}' declares duplicate outcome port '{port.Name}'.");
            }
        }
    }

    /// <summary>Refuses a descriptor whose evidence requirements are blank or duplicate within the descriptor.</summary>
    /// <param name="entry">The draft entry being validated.</param>
    public static void RefuseBlankOrDuplicateEvidence(NodeKindCatalogEntry entry)
    {
        var seenEvidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evidence in entry.Descriptor.EvidenceRequirements)
        {
            if (string.IsNullOrWhiteSpace(evidence))
            {
                throw new ProcedureNodeKindsDomainException(
                    ProcedureNodeKindsDomainException.SchemaViolation,
                    $"Descriptor '{entry.Key}' declares a blank evidence requirement.");
            }

            if (!seenEvidence.Add(evidence))
            {
                throw new ProcedureNodeKindsDomainException(
                    ProcedureNodeKindsDomainException.SchemaViolation,
                    $"Descriptor '{entry.Key}' declares duplicate evidence requirement '{evidence}'.");
            }
        }
    }

    /// <summary>Set equality of outcome-port lists compared by name and role, order-insensitive.</summary>
    /// <param name="left">First port list.</param>
    /// <param name="right">Second port list.</param>
    /// <returns>True when both lists contain the same name-role pairs.</returns>
    public static bool PortsEqual(IReadOnlyList<OutcomePort> left, IReadOnlyList<OutcomePort> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var leftByName = left.ToDictionary(static port => port.Name, StringComparer.Ordinal);
        foreach (var rightPort in right)
        {
            if (!leftByName.TryGetValue(rightPort.Name, out var leftPort))
            {
                return false;
            }

            if (!string.Equals(leftPort.Role, rightPort.Role, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Set equality of string lists, order-insensitive, ordinal-compared.</summary>
    /// <param name="left">First list.</param>
    /// <param name="right">Second list.</param>
    /// <returns>True when both lists contain the same distinct strings.</returns>
    public static bool StringSetEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var seen = new HashSet<string>(left, StringComparer.Ordinal);
        return right.All(seen.Remove) && seen.Count == 0;
    }
}

/// <summary>TryParse bridge so the caller stays a single expression.</summary>
file static class CatalogVersionParser
{
    public static CatalogVersion? TryParse(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 2
            && int.TryParse(parts[0], out var major)
            && int.TryParse(parts[1], out var minor)
            && major >= 0
            && minor >= 0
            ? new CatalogVersion(major, minor)
            : null;
    }
}
