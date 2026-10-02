namespace Comuki.Modules.Procedures.Application.Materialization.Dispatch;

/// <summary>One node's dispatch target, based on the kind's owner surface.</summary>
/// <param name="NodeId">Graph-local id of the node.</param>
/// <param name="KindKey">Catalog key of the kind.</param>
/// <param name="Depth">Topological depth (roots = 0).</param>
/// <param name="Parameters">The node's parameter set from the compiled graph.</param>
public abstract record NodeDispatch(
    string NodeId,
    string KindKey,
    int Depth,
    IReadOnlyDictionary<string, string> Parameters)
{
    /// <summary>True when this dispatch targets a work item (agent labour).</summary>
    public bool IsWorkItem => this is WorkItemDispatch;

    /// <summary>True when this dispatch targets a human decision.</summary>
    public bool IsDecision => this is DecisionDispatch;
}

/// <summary>An agent node — dispatched as a work item under claim/lease.</summary>
public sealed record WorkItemDispatch(
    string NodeId,
    string KindKey,
    int Depth,
    IReadOnlyDictionary<string, string> Parameters) : NodeDispatch(NodeId, KindKey, Depth, Parameters);

/// <summary>A human-gate node — dispatched as a decision blocking on a person.</summary>
public sealed record DecisionDispatch(
    string NodeId,
    string KindKey,
    int Depth,
    IReadOnlyDictionary<string, string> Parameters,
    int ApprovalFloor) : NodeDispatch(NodeId, KindKey, Depth, Parameters);
