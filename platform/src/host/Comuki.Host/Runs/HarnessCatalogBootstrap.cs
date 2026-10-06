using Comuki.Shared.Kernel.Harness;

namespace Comuki.Host.Runs;

/// <summary>
/// One-shot bootstrap that populates the
/// <see cref="HarnessRegistry"/> from the DI-registered
/// <see cref="IHarness"/> instances at host boot. Lives behind
/// the <see cref="IHarnessRegistryBootstrap"/> interface so the
/// production composition and the integration test composition
/// can swap it for tests that want a controlled harness catalog.
/// <para>
/// Phase 1c (add-orchestra): the host process holds the
/// capability declarations
/// (<see cref="PiHarnessCapability"/>,
/// <see cref="TestFakeHarnessCapability"/>); the runtime
/// harness — process spawn, session transport, stdin writer —
/// lives in the Translator process. The bootstrap makes the
/// host's <see cref="RunHarnessResolver"/> find the capability by
/// harness <c>Name</c> (the same identifier the runtime uses to
/// pick the implementation). Phase 8 / Instrument replaces the
/// bootstrap with a profile-frontmatter-driven catalog; the
/// resolver path stays the same.
/// </para>
/// </summary>
public interface IHarnessRegistryBootstrap
{
    /// <summary>Populate the registry once at boot.</summary>
    /// <param name="registry">The in-process catalog the resolver reads.</param>
    public void Populate(HarnessRegistry registry);
}

/// <summary>Default <see cref="IHarnessRegistryBootstrap"/>: bulk-registers every DI-registered <see cref="IHarness"/>.</summary>
/// <param name="harnesses">All harnesses the composition registered.</param>
public sealed class HarnessCatalogBootstrap(IEnumerable<IHarness> harnesses) : IHarnessRegistryBootstrap
{
    /// <inheritdoc />
    public void Populate(HarnessRegistry registry)
    {
        registry.RegisterAll(harnesses);
    }
}
