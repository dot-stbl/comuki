using Comuki.Modules.Knowledge.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// Value converters for the module's strong-typed ids (UUIDv7 → Postgres
/// <c>uuid</c>).
/// </summary>
public static class KnowledgeIdConverters
{
    /// <summary><see cref="SourceDocumentId"/> uuid converter.</summary>
    public static readonly ValueConverter<SourceDocumentId, Guid> SourceDocumentIdToUuid = new(
        static id => id.Value,
        static value => new SourceDocumentId(value));

    /// <summary><see cref="MemoryEmbeddingId"/> uuid converter.</summary>
    public static readonly ValueConverter<MemoryEmbeddingId, Guid> MemoryEmbeddingIdToUuid = new(
        static id => id.Value,
        static value => new MemoryEmbeddingId(value));

    /// <summary><see cref="WikiPageId"/> uuid converter (nullable: only Wiki documents carry a value).</summary>
    public static readonly ValueConverter<WikiPageId?, Guid?> NullableWikiPageIdToUuid = new(
        static id => id.HasValue ? id.Value.Value : null,
        static value => value.HasValue ? new WikiPageId(value.Value) : null);

    /// <summary><see cref="WikiPageId"/> uuid converter for the owned link-graph elements.</summary>
    public static readonly ValueConverter<WikiPageId, Guid> WikiPageIdToUuid = new(
        static id => id.Value,
        static value => new WikiPageId(value));
}
