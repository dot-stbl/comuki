using System.Collections.Concurrent;
using Comuki.Modules.Procedures.Application.Runtime;

namespace Comuki.Modules.Procedures.Application.ProcedureVersions.Ledger;

/// <summary>
/// In-memory <see cref="IAttemptPinLedger"/> implementation for unit
/// tests and for environments that do not yet have a database. The
/// EF-backed implementation lands with task 5.3's planned-vs-observed
/// trace persistence; this class is what the unit tests construct
/// and what the host wires as the fallback while the persistence is
/// being built. Singleton-scoped — the in-memory map survives the
/// lifetime of the host process and is shared across all admitted
/// attempts.
/// </summary>
public sealed class InMemoryAttemptPinLedger : IAttemptPinLedger
{
    private readonly ConcurrentDictionary<Guid, AttemptVersionTransition> transitions = new();

    /// <inheritdoc />
    public Task RecordAsync(
        AttemptVersionTransition transition,
        CancellationToken cancellationToken = default)
    {
        transitions.TryAdd(transition.AttemptId, transition);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AttemptVersionTransition>> ListAsync(
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(procedureKey))
        {
            throw new ProcedureRuntimeException(
                ProcedureRuntimeException.ProcedureKeyEmpty,
                "Procedure key must not be empty.");
        }

        var ordered = transitions.Values
            .Where(entry => entry.ProjectId == projectId
                && string.Equals(entry.ProcedureKey, procedureKey, StringComparison.Ordinal))
            .OrderBy(static entry => entry.RecordedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<AttemptVersionTransition>>(ordered);
    }
}
