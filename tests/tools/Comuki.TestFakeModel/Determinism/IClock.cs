namespace Comuki.TestFakeModel.Determinism;

/// <summary>
/// A clock <see cref="Scripting.FakeModelState"/> reads timestamps from.
/// Never <see cref="DateTimeOffset.UtcNow"/> directly — design.md's
/// "Determinism knobs" call for a fixed clock on every timestamp field so
/// two runs of the same fakeScript produce byte-identical recordings.
/// </summary>
public interface IClock
{
    /// <summary>The clock's current instant; advances per call per the implementation's rule.</summary>
    public DateTimeOffset UtcNow();
}
