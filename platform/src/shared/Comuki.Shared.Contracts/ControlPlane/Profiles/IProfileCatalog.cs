namespace Comuki.Shared.Contracts.ControlPlane.Profiles;

/// <summary>
/// Port to the worker-profile catalog: the control-plane <c>profiles/</c>
/// folder (Comuki defaults) or a client git overlay. The brain and the
/// dashboard read profile metadata through this port; the raw system prompt
/// stays off the wire - the Translator fetches the pinned git ref at
/// container start.
/// </summary>
public interface IProfileCatalog
{
    /// <summary>Every valid profile, ordered by key. Malformed documents are skipped with a warning, not fatal.</summary>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ProfileDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One profile by key (the document file stem, e.g. <c>implement</c>). Null when unknown.</summary>
    /// <param name="key"></param>
    /// <param name="cancellationToken"></param>
    public Task<ProfileDefinition?> GetAsync(string key, CancellationToken cancellationToken = default);
}
