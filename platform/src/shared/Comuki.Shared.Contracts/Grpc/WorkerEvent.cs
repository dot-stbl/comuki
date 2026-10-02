using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Event the worker pushes over the bidi stream, discriminated by which
/// optional field is set: <see cref="Start"/> | <see cref="Activity"/> |
/// <see cref="Report"/> | <see cref="Verify"/> | <see cref="Condition"/> |
/// <see cref="Drain"/>. Produced by the Translator from pi's
/// stream-json output, the isolated verify runner
/// (isolate-verifier-runtime 1.4), the sandbox conditions
/// (harden-pi-worker-sandbox 5.1) and the pre-complete artifact drain
/// (harden-pi-worker-sandbox 5.2); consumed by the Host gRPC service
/// and journaled.
/// </summary>
[ProtoContract]
public sealed record WorkerEvent
{
    [ProtoMember(1)]
    public StageStart? Start { get; init; }

    [ProtoMember(2)]
    public StageActivity? Activity { get; init; }

    [ProtoMember(3)]
    public StageReport? Report { get; init; }

    /// <summary>Set when the Translator's <c>VerifyRunner</c> finishes a
    /// verify-profile item; the host-side journal maps it to
    /// <c>verify.completed</c> / <c>verify.failed</c> entries.</summary>
    [ProtoMember(4)]
    public StageVerify? Verify { get; init; }

    /// <summary>Set when a sandbox stage flips a boolean condition
    /// (harden-pi-worker-sandbox 5.1, spec D6); the host-side journal
    /// maps it to a <c>worker.condition</c> entry.</summary>
    [ProtoMember(5)]
    public StageCondition? Condition { get; init; }

    /// <summary>Set just before the Translator calls
    /// <c>complete</c>/<c>fail</c> to flush artifacts accumulated
    /// during the run (harden-pi-worker-sandbox 5.2, spec D7); the
    /// host-side packager picks up already-bundled prefixes and
    /// skips them.</summary>
    [ProtoMember(6)]
    public StageDrain? Drain { get; init; }
}
