using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Knowledge;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Memory.Infrastructure.Persistence;

/// <summary>
/// Value converters for the module's strong-typed ids (UUIDv7 → Postgres
/// <c>uuid</c>).
/// </summary>
public static class MemoryIdConverters
{
    /// <summary><see cref="MemoryFactId"/> uuid converter.</summary>
    public static readonly ValueConverter<MemoryFactId, Guid> MemoryFactIdToUuid = new(
        static id => id.Value,
        static value => new MemoryFactId(value));

    /// <summary><see cref="LearningCandidateId"/> uuid converter.</summary>
    public static readonly ValueConverter<LearningCandidateId, Guid> LearningCandidateIdToUuid = new(
        static id => id.Value,
        static value => new LearningCandidateId(value));

    /// <summary><see cref="SourceDocumentId"/> uuid converter.</summary>
    public static readonly ValueConverter<SourceDocumentId, Guid> SourceDocumentIdToUuid = new(
        static id => id.Value,
        static value => new SourceDocumentId(value));

    /// <summary><see cref="MemoryEmbeddingId"/> uuid converter.</summary>
    public static readonly ValueConverter<MemoryEmbeddingId, Guid> MemoryEmbeddingIdToUuid = new(
        static id => id.Value,
        static value => new MemoryEmbeddingId(value));
}
