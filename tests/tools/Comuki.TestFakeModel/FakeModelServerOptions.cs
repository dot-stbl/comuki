using Comuki.TestFakeModel.Scripting;

namespace Comuki.TestFakeModel;

/// <summary>Construction options for <see cref="FakeModelServer"/>.</summary>
public sealed class FakeModelServerOptions
{
    /// <summary>The scripted response sequence this server instance serves.</summary>
    public required FakeScript Script { get; init; }

    /// <summary>
    /// Loopback port to bind. <c>null</c> (the default) asks the OS for
    /// an ephemeral free port — the right choice for an in-process xUnit
    /// fixture. The standalone exe host (<c>Program.cs</c>) passes an
    /// explicit port from the ad-hoc pool (17180–17200, see
    /// .agents/rules/process/ports.md).
    /// </summary>
    public int? Port { get; init; }

    /// <summary>Epoch the fixed clock starts counting from (determinism knob — design.md).</summary>
    public DateTimeOffset ClockEpoch { get; init; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>How far the fixed clock advances per observed request.</summary>
    public TimeSpan ClockStep { get; init; } = TimeSpan.FromSeconds(1);
}
