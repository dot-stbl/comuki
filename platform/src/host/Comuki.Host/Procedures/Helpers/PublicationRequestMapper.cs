using System.Text.Json;
using Comuki.Host.Procedures.Models;
using Comuki.Modules.Procedures.Application.Patches;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Layering.Model;
using Comuki.Modules.Procedures.Domain.Layering.Results;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Comuki.Modules.Procedures.Domain.Patches.Publication;

namespace Comuki.Host.Procedures.Helpers;

/// <summary>
/// Maps a wire publication request (Patch + Context + LayeredProcedure as
/// <see cref="JsonElement"/>) to the domain
/// <see cref="PublicationRequest"/> the
/// <see cref="IPublicationService"/>
/// consumes. Extracted per the no-private-methods rule.
/// 
/// <para>
/// The wire shape is the only stable contract between Studio and the
/// host. The domain types are not directly wire-serializable — they
/// hold nested record hierarchies (<see cref="GraphPatchOperation"/>'s
/// closed hierarchy, <see cref="LayeredProcedure"/>'s nested bindings)
/// whose JSON shape evolves with the spec, so the host keeps the wire
/// shape generic (<see cref="JsonElement"/>) and translates to the
/// domain at the boundary.
/// </para>
/// </summary>
internal static class PublicationRequestMapper
{
    /// <summary>Translates the wire DTO to a <see cref="PublicationRequest"/>.</summary>
    /// <param name="dto">The wire DTO — the human approver is carried inside.</param>
    public static PublicationRequest ToDomain(PublicationRequestDto dto)
    {
        var patch = DeserializePatch(dto.Patch);
        var context = DeserializeContext(dto.Context);
        var layered = DeserializeLayered(dto.LayeredProcedure);
        return new PublicationRequest(patch, dto.Approver, context, layered);
    }

    /// <summary>Deserializes the patch's <see cref="JsonElement"/> body into a
    /// <see cref="GraphPatch"/>. Operations are projected to the closed
    /// hierarchy via a <c>kind</c> discriminator field per operation.</summary>
    private static GraphPatch DeserializePatch(JsonElement element)
    {
        var id = element.GetProperty("id").GetGuid();
        var baseVersionId = element.GetProperty("baseVersionId").GetString()
            ?? throw new InvalidOperationException("GraphPatch.baseVersionId is required.");
        var projectId = element.GetProperty("projectId").GetGuid();
        var procedureKey = element.GetProperty("procedureKey").GetString()
            ?? throw new InvalidOperationException("GraphPatch.procedureKey is required.");
        var rationale = element.GetProperty("rationale").GetString()
            ?? throw new InvalidOperationException("GraphPatch.rationale is required.");
        var draftedBy = DeserializeDraftedBy(element.GetProperty("draftedBy"));
        var draftedAt = element.GetProperty("draftedAt").GetDateTimeOffset();
        var operations = DeserializeOperations(element.GetProperty("operations"));
        return new GraphPatch(
            new Modules.Procedures.Domain.Ids.GraphPatchId(id),
            baseVersionId,
            projectId,
            procedureKey,
            operations,
            rationale,
            draftedBy,
            draftedAt);
    }

    /// <summary>Deserializes the patch's <c>draftedBy</c> field.</summary>
    private static GraphPatchDraftedBy DeserializeDraftedBy(JsonElement element)
    {
        var identity = element.GetProperty("identity").GetString()
            ?? throw new InvalidOperationException("GraphPatchDraftedBy.identity is required.");
        var kindName = element.GetProperty("kind").GetString()
            ?? throw new InvalidOperationException("GraphPatchDraftedBy.kind is required.");
        var kind = kindName.ToLowerInvariant() switch
        {
            "brain" => GraphPatchDraftedKind.Brain,
            "operator" => GraphPatchDraftedKind.Operator,
            "system" => GraphPatchDraftedKind.System,
            _ => throw new InvalidOperationException($"Unknown GraphPatchDraftedBy.kind '{kindName}'."),
        };
        return new GraphPatchDraftedBy(identity, kind);
    }

    /// <summary>Deserializes the operations array, dispatching each entry to the
    /// closed hierarchy via a <c>kind</c> discriminator.</summary>
    private static IReadOnlyList<GraphPatchOperation> DeserializeOperations(JsonElement array)
    {
        var list = new List<GraphPatchOperation>(array.GetArrayLength());
        foreach (var entry in array.EnumerateArray())
        {
            var kind = entry.GetProperty("kind").GetString()
                ?? throw new InvalidOperationException("GraphPatchOperation.kind is required.");
            list.Add(kind switch
            {
                "add-node" => DeserializeAddNode(entry),
                "remove-node" => DeserializeRemoveNode(entry),
                "rewire-edge" => DeserializeRewireEdge(entry),
                "re-parameterize-node" => DeserializeReParameterizeNode(entry),
                _ => throw new InvalidOperationException($"Unknown GraphPatchOperation.kind '{kind}'."),
            });
        }
        return list;
    }

    private static GraphPatchOperation.AddNode DeserializeAddNode(JsonElement element)
    {
        var node = DeserializeNode(element.GetProperty("node"));
        return new GraphPatchOperation.AddNode(node);
    }

    private static GraphPatchOperation.RemoveNode DeserializeRemoveNode(JsonElement element)
    {
        var nodeId = element.GetProperty("nodeId").GetString()
            ?? throw new InvalidOperationException("RemoveNode.nodeId is required.");
        return new GraphPatchOperation.RemoveNode(nodeId);
    }

    private static GraphPatchOperation.RewireEdge DeserializeRewireEdge(JsonElement element)
    {
        var before = DeserializeEdge(element.GetProperty("before"));
        var after = DeserializeEdge(element.GetProperty("after"));
        return new GraphPatchOperation.RewireEdge(before, after);
    }

    private static GraphPatchOperation.ReParameterizeNode DeserializeReParameterizeNode(JsonElement element)
    {
        var nodeId = element.GetProperty("nodeId").GetString()
            ?? throw new InvalidOperationException("ReParameterizeNode.nodeId is required.");
        var newParameters = DeserializeParameters(element.GetProperty("newParameters"));
        return new GraphPatchOperation.ReParameterizeNode(nodeId, newParameters);
    }

    private static ProcedureNode DeserializeNode(JsonElement element)
    {
        var id = element.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("ProcedureNode.id is required.");
        var kindKey = element.GetProperty("kindKey").GetString()
            ?? throw new InvalidOperationException("ProcedureNode.kindKey is required.");
        var parameters = DeserializeParameters(element.GetProperty("parameters"));
        return new ProcedureNode(id, kindKey, parameters);
    }

    private static ProcedureEdge DeserializeEdge(JsonElement element)
    {
        var fromNodeId = element.GetProperty("fromNodeId").GetString()
            ?? throw new InvalidOperationException("ProcedureEdge.fromNodeId is required.");
        var fromPort = element.GetProperty("fromPort").GetString()
            ?? throw new InvalidOperationException("ProcedureEdge.fromPort is required.");
        var toNodeId = element.GetProperty("toNodeId").GetString()
            ?? throw new InvalidOperationException("ProcedureEdge.toNodeId is required.");
        return new ProcedureEdge(fromNodeId, fromPort, toNodeId);
    }

    private static IReadOnlyDictionary<string, string> DeserializeParameters(JsonElement element)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = property.Value.GetString() ?? string.Empty;
        }
        return map;
    }

    /// <summary>Deserializes the publication policy context.</summary>
    private static PublicationContext DeserializeContext(JsonElement element)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in element.GetProperty("allowedKindKeys").EnumerateArray())
        {
            allowed.Add(key.GetString()
                ?? throw new InvalidOperationException("PublicationContext.allowedKindKeys entries must be strings."));
        }
        var maxGenerations = element.GetProperty("maxGenerations").GetInt32();
        var minApprovals = element.GetProperty("minApprovals").GetInt32();
        return new PublicationContext(allowed, maxGenerations, minApprovals);
    }

    /// <summary>Deserializes the layered procedure.</summary>
    private static LayeredProcedure DeserializeLayered(JsonElement element)
    {
        var name = element.GetProperty("procedureName").GetString()
            ?? throw new InvalidOperationException("LayeredProcedure.procedureName is required.");
        var nodes = element.GetProperty("nodes").EnumerateArray()
            .Select(DeserializeNode)
            .ToList();
        var edges = element.GetProperty("edges").EnumerateArray()
            .Select(DeserializeEdge)
            .ToList();
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in element.GetProperty("allowedKindKeys").EnumerateArray())
        {
            allowed.Add(key.GetString()
                ?? throw new InvalidOperationException("LayeredProcedure.allowedKindKeys entries must be strings."));
        }
        var maxGenerations = element.GetProperty("maxGenerations").GetInt32();
        var minApprovals = element.GetProperty("minApprovals").GetInt32();
        var bindings = element.GetProperty("repositoryBindings").EnumerateArray()
            .Select(DeserializeBinding)
            .ToList();
        return new LayeredProcedure(name, nodes, edges, allowed, maxGenerations, minApprovals, bindings);
    }

    private static RepositoryBinding DeserializeBinding(JsonElement element)
    {
        var repositoryId = element.GetProperty("repositoryId").GetString()
            ?? throw new InvalidOperationException("RepositoryBinding.repositoryId is required.");
        var procedureName = element.GetProperty("procedureName").GetString()
            ?? throw new InvalidOperationException("RepositoryBinding.procedureName is required.");
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in element.GetProperty("pinnedCatalogRefs").EnumerateArray())
        {
            refs.Add(reference.GetString()
                ?? throw new InvalidOperationException("RepositoryBinding.pinnedCatalogRefs entries must be strings."));
        }
        return new RepositoryBinding(repositoryId, procedureName, refs);
    }
}
