namespace Comuki.Host.Knowledge;

/// <summary>
/// One ingest call — the body of <c>POST /api/v1/knowledge/ingest</c>.
/// <see cref="Source"/> is a wire key (<c>git</c> | <c>upload</c> |
/// <c>url</c>); the text is the raw bytes the worker chunks and
/// embeds. The request is project-scoped (projectId) or global
/// (projectId omitted).
/// </summary>
public sealed class KnowledgeIngestRequest
{
    /// <summary>Owning project; null = global corpus.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Human-readable title (file name, page heading, repo display name).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Origin kind — git | upload | url. Wire key.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Origin pointer — git URL+ref, uploaded blob id, or fetched URL.</summary>
    public string SourceRef { get; init; } = string.Empty;

    /// <summary>Detected MIME type of the original bytes (text/markdown, text/plain, …).</summary>
    public string MimeType { get; init; } = string.Empty;

    /// <summary>Raw text the worker chunks + embeds.</summary>
    public string Text { get; init; } = string.Empty;
}
