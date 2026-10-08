using Comuki.Modules.Memory.Application.Ports;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// Lexical-rank side of the hybrid retrieval: probes the
/// <c>text_tsv</c> column's existence and runs the
/// <see cref="MemoryFactSql.LexicalRankSql"/> query when it does.
/// Shares <see cref="MemoryFactHybridSearch.ColumnExistsAsync"/> and
/// <see cref="MemoryFactHybridSearch.ExecuteRankedAsync{T}"/> with the
/// vector path; differs only in the <c>@lexicalQuery</c> parameter and
/// the <see cref="LexicalRankedRow"/> reader. The probe is the
/// graceful-degradation gate: a fresh install that never applied the
/// FTS migration has no <c>text_tsv</c> at all, and the SQL fragment
/// would otherwise raise "column does not exist" — the caller
/// substitutes zero on probe miss.
/// </summary>
internal static class MemoryFactLexical
{
    public static Task<bool> HasColumnAsync(MemoryDbContext db, CancellationToken cancellationToken)
    {
        return MemoryFactHybridSearch.ColumnExistsAsync(db, MemoryFactSql.LexicalColumnExistsSql, cancellationToken);
    }

    public static async Task<IReadOnlyList<LexicalRankedRow>?> TrySearchLexicalAsync(
        MemoryDbContext db,
        string? queryText,
        MemoryFactQuery query,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Empty / blank query: skip the round-trip — an empty tsquery
        // matches no row anyway, and the caller treats an empty query
        // as "no lexical signal" (zero rank for everything). The
        // conversion itself strips surrounding quotes via
        // MemoryFactSql.LexicalQuery, so a value like " 'foo' " lands
        // as the same payload as "foo" without the caller's help.
        var lexicalQuery = string.IsNullOrEmpty(queryText)
            ? string.Empty
            : MemoryFactSql.LexicalQuery(queryText);
        return lexicalQuery is ""
            ? []
            : await MemoryFactHybridSearch.ExecuteRankedAsync(
                db,
                MemoryFactSql.LexicalRankSql,
                static (command, q) => command.Parameters.Add(
                    new NpgsqlParameter("lexicalQuery", NpgsqlTypes.NpgsqlDbType.Text) { Value = q }),
                static reader => MemoryFactSql.ReadViewWithLexicalRank(reader),
                query,
                "lexical rank search failed; using zero lexical rank",
                logger,
                cancellationToken);
    }
}
