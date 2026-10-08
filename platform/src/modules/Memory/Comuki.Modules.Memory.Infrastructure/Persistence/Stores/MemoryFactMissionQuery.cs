using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Scopes;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// Mission-scope plumbing for <see cref="MemoryFactQuery"/>. The query
/// is the same shape for every other scope; mission facts are a
/// strongly-typed pairing of <c>Scope = Mission</c> with a
/// <c>MissionId = &lt;guid&gt;</c>. This helper collapses the
/// strongly-typed surface into the (scope, subject) pair the rest of
/// the store already speaks.
/// </summary>
internal static class MemoryFactMissionQuery
{
    /// <summary>
    /// Translates a <see cref="MemoryFactQuery"/> into the (scope, subject)
    /// pair the rest of the store's filters narrow by. A query with a
    /// non-null <c>MissionId</c> is forced to <c>Scope = Mission</c> with
    /// <c>SubjectId = missionId</c> as a canonical string. A query with
    /// <c>Scope = Mission</c> but no <c>MissionId</c> is left alone — the
    /// pre-flight reachability check and the SQL filter handle the
    /// fail-closed property.
    /// </summary>
    /// <param name="query"></param>
    public static EffectiveFactScope Normalize(MemoryFactQuery query)
    {
        if (query.MissionId is { } missionId)
        {
            // Caller asked for a specific mission: scope is mission, the
            // subject is the mission id. Any other subject id the caller
            // passed in the same query is shadowed — the only way to read
            // a mission is to name the mission.
            return new EffectiveFactScope(MemoryScope.Mission, MemoryFact.CanonicalKey(missionId.ToString()));
        }

        return new EffectiveFactScope(query.Scope, query.SubjectId);
    }

    /// <summary>
    /// Builds a fresh <see cref="MemoryFactQuery"/> with the effective
    /// (scope, subject) pair plugged in. The vector and lexical paths
    /// work from the strongly-typed query, so each call site re-derives
    /// it after normalization — passing the original query would carry
    /// the un-normalized scope / subject to the SQL filter and double the
    /// narrowing.
    /// </summary>
    public static MemoryFactQuery Effective(MemoryFactQuery source, MemoryScope? effectiveScope, string? effectiveSubject)
    {
        return source with { Scope = effectiveScope, SubjectId = effectiveSubject };
    }
}
