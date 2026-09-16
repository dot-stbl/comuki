using System.Linq.Expressions;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// The sleep-cycle candidate rules for <c>memory_facts</c>: which rows the
/// consolidation worker promotes (ephemeral → standing) and which it
/// decays (standing → ephemeral). Expressed as predicates the store's
/// set-based <c>ExecuteUpdate</c> passes straight to SQL — and compiled +
/// invoked directly in unit tests, so the SQL criteria and the tested
/// logic cannot drift apart. Thresholds are documented on
/// <see cref="Configuration.MemoryConsolidationOptions"/>; the decay
/// backdate lives in <see cref="MemoryFactPolicy.DecayCreatedAt"/>.
/// </summary>
public static class MemoryFactConsolidation
{
    /// <summary>
    /// Candidates for promotion: ACTIVE ephemeral rows read at least
    /// <paramref name="readThreshold"/> times and at least
    /// <paramref name="minAge"/> old — enough reads prove the fact useful,
    /// the age keeps a fresh write's own digest reads from promoting it
    /// in the same breath.
    /// </summary>
    /// <param name="now"></param>
    /// <param name="readThreshold"></param>
    /// <param name="minAge"></param>
    public static Expression<Func<MemoryFact, bool>> PromoteCandidates(DateTimeOffset now, int readThreshold, TimeSpan minAge)
    {
        var cutoff = now - minAge;
        return fact => fact.Kind == MemoryFactKind.Ephemeral
            && fact.SupersededAt == null
            && fact.ReadCount >= readThreshold
            && fact.CreatedAt < cutoff;
    }

    /// <summary>
    /// Candidates for decay: ACTIVE standing rows not read (and not
    /// created) within <paramref name="unreadWindow"/> — a never-read
    /// row ages from its creation; the "last read or created, whichever
    /// is later" clock is what "forgotten" means here.
    /// </summary>
    /// <param name="now"></param>
    /// <param name="unreadWindow"></param>
    public static Expression<Func<MemoryFact, bool>> DecayCandidates(DateTimeOffset now, TimeSpan unreadWindow)
    {
        var cutoff = now - unreadWindow;
        return fact => fact.Kind == MemoryFactKind.Standing
            && fact.SupersededAt == null
            && (fact.LastReadAt ?? fact.CreatedAt) < cutoff;
    }
}
