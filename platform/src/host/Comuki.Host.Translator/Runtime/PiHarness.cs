using Comuki.Shared.Kernel.Harness;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Production <see cref="IHarness"/> implementation: the canonical
/// harness is <c>pi</c> in <c>--mode rpc</c> session mode (per
/// <c>specs/worker-runtime/spec.md</c> MODIFIED "Agent invocation and
/// stream parsing" body text — the v1.x <c>--no-session</c> form does
/// not exist under this change). The harness declares
/// <c>Capabilities.LiveSession = true</c> because the session transport
/// is open for <see cref="Shared.Contracts.Grpc.TurnInput"/>
/// to land authoritatively on the running pi process.
/// <para>
/// The class itself is a capability declaration only — the actual
/// process spawn lives behind <see cref="IPiRunner"/>, and the
/// session-mode stdin-write loop is Phase 8 / Instrument work
/// (<c>specs/harness-spi/spec.md</c> ADAPTER Notes). Phase 1c wires
/// the partial registration against the <see cref="Capabilities"/>
/// field only, with Phase 8 closing the rest.
/// </para>
/// </summary>
public sealed class PiHarness : IHarness
{
    /// <inheritdoc />
    public string Name => HarnessIds.Pi;

    /// <inheritdoc />
    public HarnessCapabilities Capabilities { get; } = new(liveSession: true);
}
