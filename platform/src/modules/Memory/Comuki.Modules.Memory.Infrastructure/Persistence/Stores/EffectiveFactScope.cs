using Comuki.Modules.Memory.Domain.Facts.Scopes;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// The (scope, subject) narrowing of a <see cref="Application.Ports.MemoryFactQuery"/>
/// after mission-scope plumbing collapses the strongly-typed
/// <c>MissionId</c> pair into the pair the rest of the store's filters
/// already speak. The mission-scope path always sets <see cref="Scope"/> to
/// <see cref="MemoryScope.Mission"/> with <see cref="SubjectId"/> carrying
/// the canonical mission id; every other scope keeps the caller's pair
/// verbatim.
/// </summary>
/// <param name="Scope">The effective scope for downstream filters.</param>
/// <param name="SubjectId">The effective canonical subject id inside <paramref name="Scope"/>.</param>
public sealed record EffectiveFactScope(MemoryScope? Scope, string? SubjectId);
