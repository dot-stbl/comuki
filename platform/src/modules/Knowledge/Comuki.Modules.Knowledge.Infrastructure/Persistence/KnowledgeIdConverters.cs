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
}
