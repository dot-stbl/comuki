namespace Comuki.Modules.Procedures.Application.Materialization.Dispatch.Operations;

/// <summary>A capability node — dispatched as a broker operation.</summary>
public sealed record BrokerOperationDispatch(
    string NodeId,
    string KindKey,
    int Depth,
    IReadOnlyDictionary<string, string> Parameters)
    : NodeDispatch(NodeId, KindKey, Depth, Parameters);

/// <summary>A brain-operation node — dispatched as a typed prompt.</summary>
public sealed record BrainOperationDispatch(
    string NodeId,
    string KindKey,
    int Depth,
    IReadOnlyDictionary<string, string> Parameters)
    : NodeDispatch(NodeId, KindKey, Depth, Parameters);
