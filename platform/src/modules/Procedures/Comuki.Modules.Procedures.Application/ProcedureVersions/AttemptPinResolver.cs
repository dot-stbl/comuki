using Comuki.Modules.Procedures.Application.Runtime;

namespace Comuki.Modules.Procedures.Application.ProcedureVersions;

/// <summary>
/// Default <see cref="IAttemptPinResolver"/> implementation: looks up
/// the latest published version through
/// <see cref="IProcedureVersionStore.ListByProcedureAsync"/> and
/// returns its content-addressed id. The store already orders by
/// <c>CreatedAt</c> descending, so the first entry is the then-current
/// version at the moment of the query. A retry that races with a
/// concurrent republish sees the latest commit that completed before
/// the read — task 3.3 spec scenario: "survives a concurrent
/// republish" is upheld by the store's write-once contract (task 2.4).
/// </summary>
/// <param name="versionStore">The compiled-version store (task 2.4).</param>
public sealed class AttemptPinResolver(IProcedureVersionStore versionStore) : IAttemptPinResolver
{
    /// <inheritdoc />
    public async Task<string?> ResolveCurrentVersionIdAsync(
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

        var versions = await versionStore.ListByProcedureAsync(projectId, procedureKey, cancellationToken);
        return versions.Count == 0 ? null : versions[0].VersionId;
    }
}
