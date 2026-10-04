using System.Text.Json;
using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Domain.Definitions;

namespace Comuki.Host.Procedures.Models;

/// <summary>
/// Response DTO for a compiled procedure version
/// (<c>GET /api/v1/procedures/{projectId}/{procedureKey}</c> and
/// <c>GET /api/v1/procedures/versions/{versionId}</c>). Carries the
/// identity + provenance fields the dashboard renders, plus the
/// compiled graph the Studio canvas needs to render (the runtime pins
/// the version, Studio reads the graph through generated contracts;
/// design decision 7: "Studio reads drafts and compiled versions
/// through generated contracts (kubb), never mutating git").
/// </summary>
public sealed record ProcedureVersionResponse(
    string VersionId,
    Guid ProjectId,
    string ProcedureKey,
    string CatalogVersion,
    string SourceRef,
    IReadOnlyList<ProcedureVersionResponse.ProcedureNodeDto> Nodes,
    IReadOnlyList<ProcedureVersionResponse.ProcedureEdgeDto> Edges)
{
    /// <summary>One node of the compiled graph (a kind key + parameters).</summary>
    /// <param name="Id">Graph-local identifier (edge-anchors point at it).</param>
    /// <param name="KindKey">Catalog kind the node instances.</param>
    /// <param name="Parameters">Per-kind parameter overrides.</param>
    public sealed record ProcedureNodeDto(
        string Id,
        string KindKey,
        IReadOnlyDictionary<string, string> Parameters);

    /// <summary>One typed-port wire between two nodes.</summary>
    /// <param name="FromNodeId">The source node id.</param>
    /// <param name="FromPort">The source kind's outcome port.</param>
    /// <param name="ToNodeId">The destination node id.</param>
    public sealed record ProcedureEdgeDto(
        string FromNodeId,
        string FromPort,
        string ToNodeId);

    /// <summary>Maps from the Application record to the wire shape; the graph is
    /// parsed back from the stored GraphJson so the FE receives typed
    /// nodes + edges, not raw JSON.</summary>
    public static ProcedureVersionResponse From(CompiledProcedureVersion version)
    {
        var graph = ProcedureGraphJson.Deserialize(version.GraphJson);
        var nodes = graph.Nodes
            .Select(static node => new ProcedureNodeDto(
                node.Id,
                node.KindKey,
                node.Parameters))
            .ToList();
        var edges = graph.Edges
            .Select(static edge => new ProcedureEdgeDto(
                edge.FromNodeId,
                edge.FromPort,
                edge.ToNodeId))
            .ToList();
        return new ProcedureVersionResponse(
            version.VersionId,
            version.ProjectId,
            version.ProcedureKey,
            version.CatalogVersion,
            version.SourceRef,
            nodes,
            edges);
    }
}

/// <summary>
/// Parse a compiled version's stored <c>GraphJson</c> back into a typed
/// <see cref="ProcedureGraph"/> so the response mapper can split it
/// into the wire's <c>Nodes</c> + <c>Edges</c> arrays. The
/// Application-layer <c>GraphJson.Deserialize</c> helper is internal and
/// not visible to the host; this helper is the host-side mirror.
/// </summary>
internal static class ProcedureGraphJson
{
    public static ProcedureGraph Deserialize(string graphJson)
    {
        return JsonSerializer.Deserialize<ProcedureGraph>(graphJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException(
                "Compiled version's graph JSON deserialized to null; the stored version is corrupt.");
    }
}
