using Comuki.Modules.Projects.Domain.DomainTypes;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Ports;

/// <summary>
/// Persistence port for the per-project domain-type admission policies
/// (<see cref="DomainTypeAdmission"/>). Scoped — the implementation is one
/// EF context per unit of work, like <see cref="IProjectStore"/>. Lookups
/// take the domain type as the user typed it; the implementation
/// normalizes through <see cref="DomainTypeAdmission.NormalizeKey"/> so a
/// caller never has to know the stored casing.
/// </summary>
public interface IDomainTypeAdmissionStore
{
    /// <summary>Finds the policy of one (project, domain type) pair; null when the project declared none.</summary>
    /// <param name="projectId"></param>
    /// <param name="domainType"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<DomainTypeAdmission?> FindAsync(
        ProjectId projectId,
        string domainType,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every policy of a project, oldest first.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<DomainTypeAdmission>> ListAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new policy. A second policy for the same (project, domain
    /// type) is refused by the unique index — the database is the arbiter,
    /// so concurrent creates are safe by construction.
    /// </summary>
    /// <param name="admission"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task AddAsync(DomainTypeAdmission admission, CancellationToken cancellationToken = default);

    /// <summary>Persists a mutated policy loaded through this port.</summary>
    /// <param name="admission"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task UpdateAsync(DomainTypeAdmission admission, CancellationToken cancellationToken = default);

    /// <summary>Deletes a policy; false when the row was already gone.</summary>
    /// <param name="admissionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<bool> DeleteAsync(
        DomainTypeAdmissionId admissionId,
        CancellationToken cancellationToken = default);
}
