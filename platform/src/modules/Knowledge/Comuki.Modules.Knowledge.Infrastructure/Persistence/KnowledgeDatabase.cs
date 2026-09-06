namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// Physical Knowledge database — the Postgres schema name plus every table that
/// belongs to it. Single source every <c>IEntityTypeConfiguration</c> reads;
/// no magic strings in <c>builder.ToTable(...)</c>. The migration history
/// table lives at <c>knowledge.__ef_migrations_history</c> (per the EF Core
/// Postgres convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="KnowledgeDbContext.ApplyOptions"/>.
/// </summary>
public static class KnowledgeDatabase
{
    /// <summary>Postgres schema name. The namespace.</summary>
    public const string Schema = "knowledge";

    /// <summary>Knowledge-base source documents (corpus pointer — git | upload | url).</summary>
    public const string SourceDocuments = "source_documents";

    /// <summary>Knowledge-base embedded chunks (+ pgvector embedding column, raw-SQL managed).</summary>
    public const string MemoryEmbeddings = "memory_embeddings";
}
