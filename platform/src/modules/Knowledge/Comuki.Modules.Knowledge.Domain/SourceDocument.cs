namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// One source of knowledge content: a git URL, an upload blob, an external
/// HTTP(S) URL, or a Mission-derived Wiki page. The doc worker reads bytes
/// through the kind's loader, splits the text into chunks, embeds each
/// chunk, and writes one <see cref="MemoryEmbedding"/> per chunk. The
/// <c>embedding</c> column lives outside the EF model (pgvector,
/// raw-SQL managed) — same pattern as <c>memory_facts.embedding</c>.
///
/// Wiki pages (<see cref="SourceKind.Wiki"/>) carry four extra fields the
/// other kinds do not: a stable <see cref="WikiPageId"/>, a
/// <see cref="WikiPageKind"/>, the <c>Mission</c> whose worker produced the
/// latest edit, and a link-graph adjacency list. The fields are populated
/// by the ingestor when the body carries the matching frontmatter
/// (<c>kind</c>, <c>link_graph</c>, <c>supersedes</c>); for non-Wiki kinds
/// they are null.
/// </summary>
public sealed class SourceDocument
{
    internal SourceDocument()
    {
    }

    /// <summary>Document id (UUIDv7, client-side).</summary>
    public SourceDocumentId Id { get; private set; }

    /// <summary>Owning project; null means global corpus.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>Human-readable title (file name, page heading, repo display name).</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Origin kind — git | upload | url | wiki. Wire key: <see cref="SourceKindKeys"/>.</summary>
    public SourceKind Source { get; private set; }

    /// <summary>Origin pointer — git URL+ref, uploaded blob id, fetched URL, or wiki source ref.</summary>
    public string SourceRef { get; private set; } = string.Empty;

    /// <summary>Detected MIME type of the original bytes (text/markdown, text/plain, …).</summary>
    public string MimeType { get; private set; } = string.Empty;

    /// <summary>When the document was registered.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Stable Wiki page id (UUIDv7). Null for every non-Wiki kind — the
    /// presence of this field is the canonical "this is a wiki page" mark
    /// alongside <see cref="Source"/>.
    /// </summary>
    public WikiPageId? WikiPageId { get; private set; }

    /// <summary>Wiki page kind (glossary / how-to / decision-record / incident / reference). Null for non-Wiki.</summary>
    public WikiPageKind? WikiPageKind { get; private set; }

    /// <summary>Mission whose worker produced the latest edit. Null for non-Wiki and for pages edited outside a Mission.</summary>
    public Guid? UpdatedByMissionId { get; private set; }

    /// <summary>
    /// Link-graph adjacency list as a sequence of <c>(target, kind)</c>
    /// edges. Persisted as jsonb via the EF owned-mapping on
    /// <see cref="LinkGraph"/>; the strongly-typed projection lives here
    /// for callers. Empty when the page has no edges or the kind is not
    /// Wiki.
    /// </summary>
    public IReadOnlyList<WikiPageLink> LinkGraph { get; private set; } = [];

    /// <summary>
    /// Creates a source-document row. <paramref name="sourceRef"/> carries
    /// the origin pointer that the loader uses — commit SHA, upload
    /// storage key, fetched URL, or wiki source ref. <paramref name="projectId"/>
    /// is optional; null means the corpus is global (cross-project).
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="title"></param>
    /// <param name="source"></param>
    /// <param name="sourceRef"></param>
    /// <param name="mimeType"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException">A required string is empty.</exception>
    public static SourceDocument Create(
        Guid? projectId,
        string title,
        SourceKind source,
        string sourceRef,
        string mimeType,
        DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("title must not be empty", nameof(title))
            : string.IsNullOrWhiteSpace(sourceRef)
            ? throw new ArgumentException("source ref must not be empty", nameof(sourceRef))
            : string.IsNullOrWhiteSpace(mimeType)
            ? throw new ArgumentException("mime type must not be empty", nameof(mimeType))
            : new SourceDocument
            {
                Id = SourceDocumentId.New(),
                ProjectId = projectId,
                Title = title.Trim(),
                Source = source,
                SourceRef = sourceRef.Trim(),
                MimeType = mimeType.Trim().ToLowerInvariant(),
                CreatedAt = now,
            };
    }

    /// <summary>
    /// Attaches the Wiki-specific fields to a <see cref="SourceDocument"/>
    /// that already exists in the persistence context. The caller has
    /// either built the page through <see cref="Create"/> first, or is
    /// hydrating an existing row.
    /// </summary>
    /// <param name="wikiPageId">Stable UUIDv7 id for the page.</param>
    /// <param name="kind">Page kind (glossary | how-to | decision-record | incident | reference).</param>
    /// <param name="updatedByMissionId">Mission whose worker produced the latest edit.</param>
    /// <param name="linkGraph">Adjacency list of (target, kind) edges.</param>
    /// <exception cref="InvalidOperationException">The document is not a Wiki page.</exception>
    public void AttachWikiMetadata(
        WikiPageId wikiPageId,
        WikiPageKind? kind,
        Guid? updatedByMissionId,
        IReadOnlyList<WikiPageLink> linkGraph)
    {
        if (Source != SourceKind.Wiki)
        {
            throw new InvalidOperationException(
                $"wiki metadata is only valid on SourceKind.Wiki documents; this one is {Source}");
        }

        WikiPageId = wikiPageId;
        WikiPageKind = kind;
        UpdatedByMissionId = updatedByMissionId;
        LinkGraph = linkGraph ?? [];
    }
}
