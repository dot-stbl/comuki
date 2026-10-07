using Comuki.Shared.Kernel.Harness;

namespace Comuki.Host.Runs;

/// <summary>
/// Capability declaration of the production <c>pi</c> harness
/// for the Host's <see cref="HarnessRegistry"/>. The actual
/// runtime half (<c>IHarnessRuntime</c> / session spawn) lives in
/// <c>Comuki.Host.Translator</c>; the Host process is the
/// <c>ISteerRunPort</c> caller and only needs the
/// <see cref="HarnessCapabilities"/> to decide whether the
/// <see cref="Shared.Contracts.Grpc.TurnInput"/> command is
/// authoritative. Phase 8 / Instrument replaces these
/// capability-only holders with the full harness catalog; for
/// now the <see cref="RunHarnessResolver"/> looks up
/// <see cref="Name"/> → capability by the profile key the claim
/// carries.
/// <para>
/// The runtime harness for the same name is the
/// <c>PiHarness</c> in <c>Comuki.Host.Translator.Runtime</c>
/// (<c>Capabilities.LiveSession = true</c>, the same flag this
/// class declares). The Host doesn't reference the Translator
/// project; the <c>HarnessRegistry</c> is the only seam the two
/// processes share on the harness side.
/// </para>
/// </summary>
public sealed class PiHarnessCapability : IHarness
{
    /// <inheritdoc />
    public string Name => HarnessIds.Pi;

    /// <inheritdoc />
    public HarnessCapabilities Capabilities { get; } = new(liveSession: true);
}

/// <summary>
/// Capability declaration of the in-process test fake harness
/// (production: <c>Comuki.Host.Translator.Runtime.TestFakeHarness</c>).
/// The Host side declares the capability (<c>LiveSession</c> is
/// configurable — the same flag the test fake accepts); the
/// runtime half lives in the Translator. Phase 8 / Instrument
/// replaces this with the full harness catalog entry.
/// </summary>
/// <param name="liveSession">The capability the fake declares.</param>
public sealed class TestFakeHarnessCapability(bool liveSession) : IHarness
{
    /// <inheritdoc />
    public string Name => HarnessIds.TestFakePi;

    /// <inheritdoc />
    public HarnessCapabilities Capabilities { get; } = new(liveSession: liveSession);
}
