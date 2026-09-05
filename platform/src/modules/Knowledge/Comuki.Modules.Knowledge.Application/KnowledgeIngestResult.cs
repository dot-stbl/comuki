using Comuki.Modules.Memory.Domain.Knowledge;

namespace Comuki.Modules.Knowledge.Application;

/// <summary>Result of a single ingest call — the source id + chunk count.</summary>
/// <param name="SourceDocumentId">The <see cref="SourceDocument"/> written (new or superseded).</param>
/// <param name="ChunksWritten">Chunks produced by the chunker and persisted.</param>
public sealed record KnowledgeIngestResult(SourceDocumentId SourceDocumentId, int ChunksWritten);
