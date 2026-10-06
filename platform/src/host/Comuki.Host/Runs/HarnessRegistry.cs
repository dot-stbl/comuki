using System.Collections.Concurrent;
using Comuki.Shared.Kernel.Harness;

namespace Comuki.Host.Runs;

/// <summary>
/// In-process <see cref="IHarness"/> catalog keyed by
/// <see cref="IHarness.Name"/>. Populated at construction from the
/// DI-registered <see cref="IHarness"/> instances — the same source
/// the host's <see cref="IRunHarnessResolver"/> reads and the same
/// <c>IHarness.Name</c> the Translator's runtime uses to pick the
/// matching process (<c>specs/harness-spi/spec.md</c> Requirement
/// "Harness catalog is the picker source", Phase 1c partial).
/// <para>
/// Phase 8 / Instrument expands the catalog with profile-frontmatter
/// parsing; today's source of truth is the <see cref="IHarness"/>
/// registrations the host composition makes, and the resolver
/// contract (key by name; profile-miss falls back to the canonical
/// prod harness) stays.
/// </para>
/// </summary>
public sealed class HarnessRegistry
{
    private readonly ConcurrentDictionary<string, IHarness> byName = new(StringComparer.Ordinal);

    /// <summary>
    /// Populates the catalog from every <see cref="IHarness"/> the
    /// DI container resolves. The first registration wins on
    /// collisions (<c>TryAdd</c>); the seam is idempotent across
    /// hot-reload and the same constraint every concurrent catalog
    /// carries. Today the host composition registers
    /// <see cref="PiHarnessCapability"/> (production, declares
    /// <see cref="HarnessCapabilities.LiveSession"/> = true) and
    /// <see cref="TestFakeHarnessCapability"/> with
    /// <c>liveSession: false</c> — the no-LiveSession variant
    /// matches the spec scenario at <c>worker-runtime/spec.md</c>
    /// ("TestFakeHarness declares Capabilities.LiveSession = false")
    /// and keeps the steering endpoint's follow-up-WorkItem path
    /// reachable through DI. Tests that need the LiveSession=true
    /// variant construct <c>InProcessHarness</c> directly (the
    /// private class in <c>HostSteerRunAdapterShould</c>) — that
    /// branch bypasses this registry. Phase 8 / Instrument
    /// replaces the DI-registered capabilities with a
    /// profile-frontmatter-driven catalog.
    /// </summary>
    /// <param name="harnesses">All harnesses the composition registered.</param>
    public HarnessRegistry(IEnumerable<IHarness> harnesses)
    {
        foreach (var harness in harnesses)
        {
            byName.TryAdd(harness.Name, harness);
        }
    }

    /// <summary>
    /// Resolves the harness for <paramref name="name"/>, or
    /// <c>null</c> when the name has not been registered. The
    /// resolver is a single read; the orchestration schema is the
    /// dominant cost in the operator-endpoint critical path.
    /// </summary>
    /// <param name="name">Harness name (e.g. <see cref="HarnessIds.Pi"/>
    /// or <see cref="HarnessIds.TestFakePi"/>).</param>
    public IHarness? FindByName(string name)
    {
        return byName.TryGetValue(name, out var harness)
            ? harness
            : null;
    }
}
