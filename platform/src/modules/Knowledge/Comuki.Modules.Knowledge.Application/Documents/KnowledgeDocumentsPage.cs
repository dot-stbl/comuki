namespace Comuki.Modules.Knowledge.Application.Documents;

/// <summary>
/// Wire row of one knowledge source document (the library page) with its
/// chunk aggregates. Money-free, vector-free — text metadata only.
/// </summary>
/// <param name="Id">Document id.</param>
/// <param name="ProjectId">Owning project; null = global corpus.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="Source">Origin kind wire key: <c>git</c> | <c>upload</c> | <c>url</c>.</param>
/// <param name="SourceRef">Origin pointer (git URL+ref, blob id, fetched URL).</param>
/// <param name="MimeType">Detected MIME type of the original bytes.</param>
/// <param name="ChunkCount">Embedded chunks cut from the document.</param>
/// <param name="TokenCount">Approximate token total across chunks.</param>
/// <param name="CreatedAt">When the document was registered.</param>
public sealed record KnowledgeDocumentSummary(
    Guid Id,
    Guid? ProjectId,
    string Title,
    string Source,
    string SourceRef,
    string MimeType,
    int ChunkCount,
    long TokenCount,
    DateTimeOffset CreatedAt);

/// <summary>One page of documents plus the paging envelope (same shape as the runs page).</summary>
/// <param name="Items">Page rows.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="Total">Total documents matching the filter.</param>
public sealed record KnowledgeDocumentsPage(
    IReadOnlyList<KnowledgeDocumentSummary> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>Page-bounds normalization shared by the reader and the endpoint.</summary>
public static class KnowledgeDocumentsPaging
{
    /// <summary>Clamps the page request to the same bounds the runs page uses: page ≥ 1, size in [1, 100].</summary>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        return (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
    }
}
