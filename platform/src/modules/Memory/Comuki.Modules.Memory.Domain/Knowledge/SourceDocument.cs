namespace Comuki.Modules.Memory.Domain.Knowledge;

/// <summary>
/// One source of knowledge content: a git URL, an upload blob, or an
/// external HTTP(S) URL. The doc worker reads bytes through the kind's
/// loader, splits the text into chunks, embeds each chunk, and writes
/// one <see cref="MemoryEmbedding"/> per chunk. The
/// <c>embedding</c> column lives outside the EF model (pgvector,
/// raw-SQL managed) — same pattern as <c>memory_facts.embedding</c>.
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

    /// <summary>Origin kind — git | upload | url. Wire key: <see cref="SourceKindKeys"/>.</summary>
    public SourceKind Source { get; private set; }

    /// <summary>Origin pointer — git URL+ref, uploaded blob id, or fetched URL.</summary>
    public string SourceRef { get; private set; } = string.Empty;

    /// <summary>Detected MIME type of the original bytes (text/markdown, text/plain, …).</summary>
    public string MimeType { get; private set; } = string.Empty;

    /// <summary>When the document was registered.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a source-document row. <paramref name="sourceRef"/> carries
    /// the origin pointer that the loader uses — commit SHA, upload
    /// storage key, or fetched URL. <paramref name="projectId"/> is
    /// optional; null means the corpus is global (cross-project).
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
}
