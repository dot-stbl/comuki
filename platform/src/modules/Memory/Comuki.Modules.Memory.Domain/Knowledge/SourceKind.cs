namespace Comuki.Modules.Memory.Domain.Knowledge;

/// <summary>
/// Origin of a <see cref="SourceDocument"/>. Wire key:
/// <see cref="SourceKindKeys"/>. The kind shapes how the doc worker
/// reaches the bytes — git URL clone + checkout, upload stream from
/// the ingest endpoint, or HTTP GET for an external URL.
/// </summary>
public enum SourceKind
{
    /// <summary>A git repository URL (commit or ref pinned per source_ref).</summary>
    Git = 1,

    /// <summary>An uploaded blob (multipart/form-data to the ingest endpoint).</summary>
    Upload = 2,

    /// <summary>An external HTTP(S) URL the worker fetches once and snapshots.</summary>
    Url = 3,
}
