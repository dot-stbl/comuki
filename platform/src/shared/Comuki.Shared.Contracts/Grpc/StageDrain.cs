using ProtoBuf;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// Stage drain event (harden-pi-worker-sandbox 5.2, spec D7). Sent
/// just before the Translator calls the REST <c>complete</c> /
/// <c>fail</c> endpoint: every artifact the worker accumulated during
/// the run (pins from the worker SDK, the working tree snapshot, the
/// result file) gets flushed here so the Host's
/// <c>RunArtifactPackager</c> can pick it up and skip its own
/// re-bundling. The drain is best-effort — a transport failure is
/// logged on the worker side and <c>complete</c> still runs; the
/// packager's idempotence (<c>IsBundledAsync</c>) is the safety net.
/// </summary>
[ProtoContract]
public sealed record StageDrain
{
    /// <summary>Work item id this drain belongs to (matches the claim).</summary>
    [ProtoMember(1)]
    public string WorkItemId { get; init; } = string.Empty;

    /// <summary>
    /// Artifacts the worker drained before <c>complete</c> / <c>fail</c>.
    /// The host-side packager skips any <c>ArtifactPointer</c> whose
    /// <c>Name</c> is already in the bundle, so the drain and the
    /// packager can run independently without duplicating uploads.
    /// </summary>
    [ProtoMember(2)]
    public IReadOnlyList<string> Artifacts { get; init; } = [];
}
