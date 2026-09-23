using Comuki.TestFakeModel.Scripting.Model;

namespace Comuki.TestFakeModel.Hosting;

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

    /// <summary>
    /// Address Kestrel binds. Defaults to loopback-only — right for an
    /// in-process xUnit fixture (nothing outside this process should ever
    /// reach it). <c>Program.cs</c>'s standalone/container host overrides
    /// this to <c>0.0.0.0</c>: a container's loopback interface isn't
    /// reachable through the host's port mapping, only its bridge
    /// interface is.
    /// </summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>Epoch the fixed clock starts counting from (determinism knob — design.md).</summary>
    public DateTimeOffset ClockEpoch { get; init; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>How far the fixed clock advances per observed request.</summary>
    public TimeSpan ClockStep { get; init; } = TimeSpan.FromSeconds(1);
}
