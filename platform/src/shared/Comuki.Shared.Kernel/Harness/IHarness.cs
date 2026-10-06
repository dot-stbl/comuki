namespace Comuki.Shared.Kernel.Harness;

/// <summary>
/// The capability-side of the harness abstraction (add-orchestra
/// Phase 1c — <c>specs/harness-spi/spec.md</c> Requirement
/// "IHarness is the abstraction"). The runtime side
/// (<c>IHarnessRuntime</c> in the Translator project) adds the
/// session-spawn method; the shared kernel deliberately does not
/// depend on the runtime layer (the runtime lives in
/// <c>Comuki.Host.Translator</c>; the events it streams are
/// <c>Comuki.Host.Translator.Parsing.PiEvent</c> — both below the
/// shared kernel in the dependency graph).
/// <para>
/// The Host's harness resolver reads <see cref="Capabilities"/>;
/// the Translator's <c>PiPump</c> reads the runtime side; the
/// WorkerCommandHandler reads the active session through the
/// <c>WorkerRun</c> (no direct harness reference at command-handling
/// time). The <c>HarnessRegistry</c> on the Host side keys by
/// <see cref="Name"/>; the Translator side keys by profile the
/// same way the existing <c>envClass</c> label matches.
/// </para>
/// <para>
/// Two implementations ship in 1c:
/// <list type="bullet">
///   <item>The production <c>PiHarness</c> in
///   <c>Comuki.Host.Translator.Runtime</c> — declares
///   <c>Capabilities.LiveSession = true</c> and spawns
///   <c>pi --mode rpc</c>; the v1.x one-shot
///   <c>pi -p BRIEF --no-session</c> path is removed.</item>
///   <item>The in-process <c>TestFakeHarness</c> in
///   <c>Comuki.Host.Translator.Runtime</c> — the test fake with
///   configurable <c>LiveSession</c> flag; the external
///   <c>Comuki.TestFakePi</c> binary stays as the runtime path the
///   runner uses when <c>TranslatorOptions.PiExecutable</c> points at
///   it.</item>
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
    /// <c>Capabilities.LiveSession</c> field is the single source
    /// of truth for "can a steer land here authoritatively?" per
    /// <c>openspec/changes/add-orchestra/specs/worker-runtime/spec.md</c>
    /// Requirement "Capabilities.LiveSession is the single source of
    /// truth for steering".
    /// </summary>
    public HarnessCapabilities Capabilities { get; }
}
