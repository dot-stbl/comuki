using Comuki.Shared.Kernel.Harness;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// In-process <see cref="IHarness"/> for unit and integration tests
/// (add-orchestra Phase 1c — <c>specs/worker-runtime/spec.md</c>
/// Requirement "Harness abstraction is the Translator public surface",
/// scenario "TestFakeHarness is the second implementation").
/// Mirrors the external <c>Comuki.TestFakePi</c> binary by name; the
/// external process remains the runtime path the runner uses when
/// <c>TranslatorOptions.PiExecutable</c> points at it, and the
/// in-process <see cref="TestFakeHarness"/> is what the steering
/// endpoint's harness resolver returns in the
/// <c>Host.Unit.Steer</c> tests.
/// <para>
/// Two surfaces live side by side in 1c:
/// <list type="bullet">
///   <item><c>LiveSession = true</c> — the in-process test fake
///   with the live session capability. Used to exercise the
///   <c>TrySendTurnInput</c> path through the steering endpoint and
///   prove the integration covers the Phase 1c wire shape
///   end-to-end.</item>
///   <item><c>LiveSession = false</c> — the "horse-less injector"
///   surface (per <c>specs/session/spec.md</c> scenario "a
///   horse-less injector is not authoritative"). Phase 8 / Instrument
///   owns the negative-case harness as a separate test surface; in
///   1c the live-session-declaring fake is the load-bearing path,
///   and the false path is reachable through the existing
///   <c>HostSteerRunAdapter</c> follow-up branch.</item>
/// </list>
/// </para>
/// </summary>
/// <param name="liveSession">The capability the fake declares. The
/// production <see cref="PiHarness"/> is always <c>true</c>; the
/// test surface is the value the unit test pins (per
/// <c>specs/worker-runtime/spec.md</c> scenarios "Test-fake harness
/// declares LiveSession = false" and "Test-fake harness rejects
/// authoritative session turns").</param>
public sealed class TestFakeHarness(bool liveSession) : IHarness
{
    /// <inheritdoc />
    public string Name => HarnessIds.TestFakePi;

    /// <inheritdoc />
    public HarnessCapabilities Capabilities { get; } = new(liveSession: liveSession);
}
