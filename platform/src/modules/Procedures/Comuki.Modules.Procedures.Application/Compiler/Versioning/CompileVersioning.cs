using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Definitions;

namespace Comuki.Modules.Procedures.Application.Compiler.Versioning;

/// <summary>
/// The compile gate's version artifact: the content-addressed version
/// id (SHA-256 over a canonical serialization) and the storage JSON of
/// the validated graph. Extracted per the no-private-methods rule.
/// </summary>
internal static class CompileVersioning
{
    /// <summary>
    /// Computes the content-addressed version id: SHA-256 over the
    /// canonical serialization of the definition plus the pinned
    /// catalog version. Identical inputs always produce the identical
    /// id (spec requirement "Deterministic compile gate").
    /// </summary>
    /// <param name="definition">The validated procedure definition (compile-gate input shape).</param>
    /// <param name="catalog">The pinned node-kind catalog.</param>
    /// <returns>Lowercase hex SHA-256 of the canonical input.</returns>
    public static string ComputeVersionId(Model.ProcedureDefinition definition, NodeKindCatalog catalog)
    {
        var canonical = JsonSerializer.Serialize(new CanonicalCompileInput(
            definition.ProjectId,
            definition.ProcedureKey,
            [.. definition.Graph.Nodes.Select(static node => new CanonicalNode(node.Id, node.KindKey)).OrderBy(static node => node.Id, StringComparer.Ordinal)],
            [.. definition.Graph.Edges.Select(static edge => new CanonicalEdge(edge.FromNodeId, edge.FromPort, edge.ToNodeId)).OrderBy(static edge => edge.FromNodeId, StringComparer.Ordinal).ThenBy(static edge => edge.FromPort, StringComparer.Ordinal)],
            catalog.Version),
            JsonSerializerOptions.Web);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Serializes the validated graph for storage and runtime materialization.</summary>
    /// <param name="graph">The validated procedure graph.</param>
    /// <returns>Camel-case JSON of the graph.</returns>
    public static string SerializeGraph(ProcedureGraph graph)
    {
        return JsonSerializer.Serialize(graph, JsonSerializerOptions.Web);
    }
}

/// <summary>Canonical serialization shape — sorted, deterministic, no timestamps.</summary>
internal sealed record CanonicalCompileInput(
    Guid ProjectId,
    string ProcedureKey,
    IReadOnlyList<CanonicalNode> Nodes,
    IReadOnlyList<CanonicalEdge> Edges,
    string CatalogVersion);

/// <summary>Canonical node projection — id and kind key only.</summary>
internal sealed record CanonicalNode(string Id, string KindKey);

/// <summary>Canonical edge projection — endpoints and source port only.</summary>
internal sealed record CanonicalEdge(string FromNodeId, string FromPort, string ToNodeId);
