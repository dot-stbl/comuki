namespace Comuki.Shared.Contracts.ControlPlane.Skills;

/// <summary>
/// Port to the control-plane skill catalog: the <c>skills/</c> folder
/// (Comuki defaults) or a client git overlay. The brain and the worker SDK
/// list skill metadata through this port; the body is fetched by the
/// Translator at the same time as profiles (the same git ref pins both).
/// </summary>
public interface ISkillCatalog
{
    /// <summary>Every valid skill, ordered by key. Malformed documents are skipped with a warning, not fatal.</summary>
    public Task<IReadOnlyList<SkillDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One skill by key (the document's directory name, e.g. <c>citation-cleanup</c>). Null when unknown.</summary>
    /// <param name="key"></param>
    /// <param name="cancellationToken"></param>
    public Task<SkillDefinition?> GetAsync(string key, CancellationToken cancellationToken = default);
}
