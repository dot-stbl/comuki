namespace Comuki.Modules.Procedures.Domain.Catalog;

/// <summary>
/// Versioned root of the procedure-node-kind catalog: a catalog version
/// (semver-like — major.minor, content-addressed), the git ref the version
/// was compiled from, and the descriptors themselves. A compiled procedure
/// pins the catalog version it resolved against — later catalog changes do
/// not affect active pins (spec scenario: "Republished procedure keeps its
/// catalog pin"). Adding a kind or altering ports/parameters/evidence
/// produces a new version; retracting a kind referenced by any published
/// procedure is refused (catalog validation, task 1.2).
/// </summary>
/// <param name="Version">Catalog version identifier (<c>major.minor</c> format).</param>
/// <param name="SourceRef">Git ref the catalog was loaded from.</param>
/// <param name="Entries">The descriptors this version contains.</param>
public sealed record NodeKindCatalog(
    string Version,
    string SourceRef,
    IReadOnlyList<NodeKindCatalogEntry> Entries)
{
    /// <summary>Baseline v1 — the initial fixed catalog shipped with the platform.</summary>
    public const string BaselineVersion = "1.0";

    /// <summary>The folder name inside the control-plane root that holds node-kind markdown files.</summary>
    public const string FolderName = "procedure-node-kinds";

    /// <summary>Look up an entry by key; null when absent.</summary>
    /// <param name="key"></param>
    public NodeKindCatalogEntry? Find(string key)
    {
        return Entries.FirstOrDefault(
            entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
    }
}
