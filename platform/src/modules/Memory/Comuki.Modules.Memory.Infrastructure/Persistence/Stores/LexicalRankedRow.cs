using Comuki.Modules.Memory.Application.Views;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// One row of the lexical-path hybrid-retrieval query: the projected
/// <see cref="MemoryFactView"/> paired with the row's
/// <c>ts_rank</c> score. The view carries the entity's id / scope /
/// subject / kind / topic / text / source / created-by / created-at;
/// <see cref="LexicalRank"/> is the
/// <c>ts_rank(text_tsv, plainto_tsquery('simple', @lexicalQuery))</c>
/// value the SQL emits.
/// </summary>
/// <param name="View">The fact row read from Postgres.</param>
/// <param name="LexicalRank">The row's <c>ts_rank</c> score.</param>
public sealed record LexicalRankedRow(MemoryFactView View, float LexicalRank);
