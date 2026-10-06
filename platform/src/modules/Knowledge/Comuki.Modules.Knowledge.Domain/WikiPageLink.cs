namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// One edge in a Wiki page's link graph: the target page id plus the
/// relationship kind. Persisted as a single jsonb entry per edge in
/// <see cref="SourceDocument.LinkGraph"/>.
/// </summary>
/// <param name="TargetPageId">The page the current one points at.</param>
/// <param name="Kind">How the current page relates to the target.</param>
public sealed record WikiPageLink(WikiPageId TargetPageId, WikiPageLinkKind Kind);
