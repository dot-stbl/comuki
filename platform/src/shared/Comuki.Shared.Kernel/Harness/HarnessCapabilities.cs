namespace Comuki.Shared.Kernel.Harness;

/// <summary>
/// Capability advertisement the harness exposes to the platform
/// (add-orchestra Phase 1c — <c>openspec/changes/add-orchestra/
/// specs/harness-spi/spec.md</c> Requirement "Capabilities is a record
/// struct"). <c>readonly record struct</c> per the spec: a single
/// <see cref="LiveSession"/> field for the Phase 1c surface; later
/// phases (Phase 8 / Instrument) extend the struct with
/// <c>StreamingToolResults</c>, <c>ResumeAcrossRestarts</c> and
/// similar without changing the read sites that already exist.
/// <para>
/// The Translator reads the value at worker start and chooses the
/// spawn strategy: <see cref="LiveSession"/> = <c>true</c> opens the
/// bidi command channel and accepts
/// <c>Comuki.Shared.Contracts.Grpc.TurnInput</c>; <c>false</c> does
/// not. The field is the single source of truth for "can a steer
/// land here authoritatively?" — the Translator does not consult any
/// other field for the steering decision (the
/// <c>add-orchestra</c> Phase 1c requirement).
/// </para>
/// </summary>
public readonly record struct HarnessCapabilities
{
    /// <summary>
    /// Build the capability set for a harness. The constructor is the
    /// only public surface; later phases add named factory methods
    /// (e.g. <c>WithStreamingToolResults</c>) without changing the
    /// positional constructor's contract.
    /// </summary>
    /// <param name="liveSession">True when the harness accepts the
    /// gRPC <c>TurnInput</c> variant as an authoritative session turn
    /// on its live process.</param>
    public HarnessCapabilities(bool liveSession)
    {
        LiveSession = liveSession;
    }

    /// <summary>
    /// <c>true</c> when the harness exposes a live session the
    /// platform can steer into via the bidi command channel. The
    /// default capability set (<c>new HarnessCapabilities()</c>) is
    /// <c>LiveSession = false</c> — a horse-less injector, by the
    /// <c>specs/session/spec.md</c> scenario "a horse-less injector
    /// is not authoritative" wording.
    /// </summary>
    public bool LiveSession { get; }
}
