using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// The lease of the worker's item expired and the reaper took it back:
/// the worker must stop immediately and must NOT complete/fail the item —
/// ownership is gone.
/// </summary>
[ProtoContract]
public sealed record LeaseExpired;
