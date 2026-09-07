namespace Comuki.Shared.Contracts.Realtime;

/// <summary>
/// Marks a record or class as a wire-format contract for the C#->TS
/// codegen emitter (<see cref="RealtimeContractEmitter"/>). Each property
/// of the annotated type becomes a TypeScript property of the same name
/// and C#-shape: <c>Guid</c>/<c>Guid?</c> map to <c>string</c>/<c>string | null</c>,
/// <c>string?</c> to <c>string | null</c>, <c>long</c> to <c>number</c>,
/// <c>bool</c> to <c>boolean</c>. Properties flagged with
/// <see cref="RealtimeContractJsonNameAttribute"/> override the wire
/// casing. The emitter is intentionally tiny (no Roslyn, no source
/// generators) — it is the smallest thing that can render a
/// single-source-of-truth contract for the dashboard SignalR client.
/// </summary>
/// <remarks>
/// Reserved for types in the realtime surface; the engine gRPC contracts
/// use a different wire format (protobuf-net) and a different
/// emitter (kubb + openapi). The two emitters must stay disjoint to
/// keep this slice self-contained.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class RealtimeContractAttribute : Attribute;
