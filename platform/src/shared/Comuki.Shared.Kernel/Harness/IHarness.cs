namespace Comuki.Shared.Kernel.Harness;

/// <summary>
/// The harness abstraction (add-orchestra Phase 1c — <c>specs/harness-spi/
/// spec.md</c> Requirement "IHarness is the abstraction"). Phase 8 /
/// Instrument owns the full SPI (env resolution, spawn, event parsing,
/// slot binding); Phase 1c wires the *partial* surface —
/// <see cref="Name"/> and <see cref="Capabilities"/> — that the
/// steering endpoint reads to decide whether the gRPC
/// <c>TurnInput</c> variant lands authoritatively on the worker. The
/// runtime surface (<c>IPiRunner</c>) stays alongside:
/// <see cref="IHarness"/> declares the capability, the runner
/// spawns the process.
/// <para>
/// Two implementations ship in 1c:
/// <list type="bullet">
///   <item>The production <c>PiHarness</c> in
///   <c>Comuki.Host.Translator.Runtime</c> — declares
///   <c>Capabilities.LiveSession = true</c>; the v1.x one-shot
///   <c>pi -p BRIEF --no-session</c> path is removed (the harness-spi
///   surface runs pi in <c>--mode rpc</c> session mode; Phase 8
///   closes the actual stdin-write loop).</item>
///   <item>The in-process <c>TestFakeHarness</c> in
///   <c>Comuki.Host.Translator.Runtime</c> — the test fake,
///   configurable <c>LiveSession</c> flag; the external
///   <c>Comuki.TestFakePi</c> binary stays as the runtime path
///   the runner uses when <c>TranslatorOptions.PiExecutable</c>
///   points at it.</item>
/// </list>
/// </para>
/// </summary>
public interface IHarness
{
    /// <summary>
    /// Stable name used as the <c>HarnessProfile</c> marker
    /// (<c>openspec/changes/add-orchestra/specs/harness-spi/spec.md</c>
    /// Requirement "Profile declares its harness"). Today:
    /// <c>"pi"</c> or <c>"test-fake-pi"</c>. A profile without an
    /// explicit <c>harness:</c> frontmatter defaults to <c>"pi"</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Capability advertisement. The platform reads the value at
    /// worker start and chooses the spawn strategy; the
    /// <c>Capabilities.LiveSession</c> field is the single source of
    /// truth for "can a steer land here authoritatively?" per
    /// <c>specs/session/spec.md</c> Requirement
    /// "Capabilities.LiveSession is the single source of truth for
    /// steering".
    /// </summary>
    public HarnessCapabilities Capabilities { get; }
}
