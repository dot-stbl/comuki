using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Exceptions;
using Comuki.Modules.Procedures.Domain.Kinds;
using Comuki.Modules.Procedures.Domain.Kinds.Types;

namespace Comuki.Modules.Procedures.Infrastructure.Loading;

/// <summary>
/// Pure materialiser for parsed procedure-node-kind documents. Takes a
/// <see cref="NodeKindDescriptorDocument"/> and produces the typed
/// <see cref="NodeKindDescriptor"/>, surfacing a
/// <see cref="ProcedureNodeKindsDomainException"/> when the owner surface
/// is missing (spec scenario: "Descriptor missing an owner") or when the
/// port/idempotency wire form is unrecognised.
/// </summary>
internal static class NodeKindCatalogMaterializer
{
    /// <summary>
    /// Promote a parsed document to the typed <see cref="NodeKindDescriptor"/>.
    /// Surfaces a <see cref="ProcedureNodeKindsDomainException"/> when the
    /// owner surface is missing (spec scenario: "Descriptor missing an owner")
    /// or when the port/idempotency wire form is unrecognised.
    /// </summary>
    /// <param name="key">File-stem key.</param>
    /// <param name="document">Parsed frontmatter document.</param>
    public static NodeKindDescriptor Materialize(string key, NodeKindDescriptorDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.OwnerSurface))
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.OwnerSurfaceMissing,
                $"Procedure-node-kind descriptor '{key}' is missing an owner surface (owner: <WorkItem|Decision|BrokerOperation|BrainOperation>).");
        }

        var owner = ParseOwner(key, document.OwnerSurface);
        var idempotency = ParseIdempotency(key, document.Idempotency);

        var ports = new List<OutcomePort>(document.OutcomePorts.Count);
        foreach (var portName in document.OutcomePorts)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                continue;
            }

            ports.Add(OutcomePort.Define(portName, role: string.Empty));
        }

        return new NodeKindDescriptor(
            Key: key,
            Title: document.Title,
            Description: document.Description,
            OwnerSurface: owner,
            ParameterSchema: document.ParameterSchema,
            OutcomePorts: ports,
            EvidenceRequirements: document.EvidenceRequirements,
            RiskClass: document.RiskClass,
            Idempotency: idempotency,
            ApprovalFloor: document.ApprovalFloor,
            EditionsFeatureKey: document.EditionsFeatureKey is { Length: > 0 }
                ? document.EditionsFeatureKey
                : null);
    }

    public static NodeKindOwnerSurface ParseOwner(string key, string wire)
    {
        try
        {
            return NodeKindOwnerSurface.FromWire(wire);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.OwnerSurfaceMissing,
                $"Procedure-node-kind descriptor '{key}' declares unknown owner surface '{wire}'; expected WorkItem|Decision|BrokerOperation|BrainOperation.");
        }
    }

    public static NodeKindIdempotency ParseIdempotency(string key, string wire)
    {
        try
        {
            return NodeKindIdempotency.FromWire(wire);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ProcedureNodeKindsDomainException(
                ProcedureNodeKindsDomainException.OwnerSurfaceMissing,
                $"Procedure-node-kind descriptor '{key}' declares unknown idempotency '{wire}'; expected optional|required|inherent.");
        }
    }

    /// <summary>
    /// Read the catalog version's source ref from <c>control-plane/{FolderName}/VERSION</c>
    /// when present, or fall back to <c>unknown</c>. The version root is
    /// pinned per published procedure; we surface a known string here so
    /// downstream tooling can render it without guessing.
    /// </summary>
    /// <param name="controlPlaneRoot">The control-plane root the catalog was loaded from.</param>
    /// <returns>The VERSION file's trimmed content, or <c>unknown</c>.</returns>
    public static string ResolveSourceRef(string controlPlaneRoot)
    {
        var versionFile = Path.Combine(
            controlPlaneRoot,
            NodeKindCatalog.FolderName,
            "VERSION");
        return File.Exists(versionFile)
            ? File.ReadAllText(versionFile).Trim()
            : "unknown";
    }
}
