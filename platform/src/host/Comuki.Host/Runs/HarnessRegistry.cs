using System.Collections.Concurrent;
using Comuki.Shared.Kernel.Harness;

namespace Comuki.Host.Runs;

/// <summary>
/// In-process <see cref="IHarness"/> catalog populated at host boot
/// from the registered <see cref="IHarness"/> instances
/// (<c>specs/harness-spi/spec.md</c> Requirement "Harness catalog is the
/// picker source", Phase 1c partial). Today the resolver is the only
/// consumer; Phase 8 / Instrument expands it to drive the dashboard
/// project-settings-drawer harness picker.
/// <para>
/// The mapping is by <c>profile key</c> (a stable string the claim
/// stamp carries). Two profiles with the same key are not allowed —
/// the composition root is the only place the registry is
/// populated, and the test fakes register distinct keys.
/// </para>
/// </summary>
public sealed class HarnessRegistry
{
    private readonly ConcurrentDictionary<string, IHarness> byProfileKey = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers <paramref name="harness"/> under
    /// <paramref name="profileKey"/>. The first registration wins;
    /// later registrations are logged and dropped to keep the seam
    /// idempotent across hot-reload (the test surface relies on
    /// idempotence when a unit test creates a second harness for the
    /// same key).
    /// </summary>
    /// <param name="profileKey">Stable profile key the claim stamp carries.</param>
    /// <param name="harness">Harness implementation to look up by key.</param>
    public void Register(string profileKey, IHarness harness)
    {
        byProfileKey.TryAdd(profileKey, harness);
    }

    /// <summary>
    /// Bulk registration by harness <see cref="IHarness.Name"/>: every
    /// entry's <c>Name</c> becomes the lookup key. Used at boot
    /// from the <see cref="IHarness"/> instances the host
    /// composition registers
    /// (<see cref="PiHarnessCapability"/>,
    /// <see cref="TestFakeHarnessCapability"/>). The first wins on
    /// collisions; the surface is idempotent across hot-reload
    /// (the same constraint as <see cref="Register"/>).
    /// </summary>
    /// <param name="harnesses">Harnesses to register by their <c>Name</c>.</param>
    public void RegisterAll(IEnumerable<IHarness> harnesses)
    {
        foreach (var harness in harnesses)
        {
            byProfileKey.TryAdd(harness.Name, harness);
        }
    }

    /// <summary>
    /// Resolves the harness for <paramref name="profileKey"/>, or
    /// <c>null</c> when the key has not been registered. The resolver
    /// is a single read; the orchestration schema is the dominant
    /// cost in the operator-endpoint critical path.
    /// </summary>
    /// <param name="profileKey">Profile key the claim stamp carries.</param>
    public IHarness? FindByProfileKey(string profileKey)
    {
        return byProfileKey.TryGetValue(profileKey, out var harness)
            ? harness
            : null;
    }

    /// <summary>
    /// Snapshot of the registered harnesses. Today the catalog is
    /// used by the steering resolver only; Phase 8 wires the dashboard
    /// picker against this surface.
    /// </summary>
    public IReadOnlyCollection<IHarness> Snapshot()
    {
        return [.. byProfileKey.Values];
    }
}
