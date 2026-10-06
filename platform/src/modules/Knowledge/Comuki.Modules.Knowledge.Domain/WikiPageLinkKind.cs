namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// The kind of an edge in a Wiki page's link graph. Wire key:
/// <see cref="WikiPageLinkKindKeys"/>.
/// </summary>
public enum WikiPageLinkKind
{
    /// <summary>Sibling reference — the target page is related but not authoritative.</summary>
    SeeAlso = 1,

    /// <summary>The current page supersedes the target (replacement edge).</summary>
    Supersedes = 2,

    /// <summary>The current page was derived from the target (provenance edge).</summary>
    DerivedFrom = 3,
}
