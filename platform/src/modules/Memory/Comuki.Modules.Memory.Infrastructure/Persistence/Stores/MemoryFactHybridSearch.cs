using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// Shared runner for the two halves of <see cref="EfMemoryStore"/>'s
/// hybrid retrieval. Both <see cref="MemoryFactVectors"/> and
/// <see cref="MemoryFactLexical"/> need to do the same dance: open the
/// connection, build a <c>DbCommand</c>, bind the path-specific
/// parameters, then bind the object-axis scope parameters (which run
/// outside EF's <c>HasQueryFilter</c> and have to be reproduced directly
/// here), then the narrowing <c>@scope</c> / <c>@subject</c> / <c>@kind</c>
/// filters, then the LIMIT, then read rows of the right type until
/// the reader runs dry. <see cref="ExecuteRankedAsync{T}"/> centralises
/// that dance: callers pass the path-specific <see cref="NpgsqlParameter"/>
///(s) plus a row reader, and the runner binds the rest exactly once.
/// </summary>
internal static class MemoryFactHybridSearch
{
    /// <summary>
    /// Probes one column-existence query. The cosine path and the
    /// lexical path each call this with their own SQL constant; the
    /// runner owns the connection / command / scalar / cleanup
    /// mechanics exactly once.
    /// </summary>
    /// <param name="db">The context whose connection the probe runs on.</param>
    /// <param name="probeSql">
    /// Column-existence probe SQL — a constant from
    /// <see cref="MemoryFactSql"/>, never user input. CA2100 is
    /// suppressed for the same reason as in
    /// <see cref="ExecuteRankedAsync{T}"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the probe.</param>
    public static async Task<bool> ColumnExistsAsync(
        MemoryDbContext db,
        string probeSql,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // SQL injection — probeSql is always a constant from MemoryFactSql
            command.CommandText = probeSql;
#pragma warning restore CA2100
            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Runs one ranked search: binds the path-specific parameters
    /// (<paramref name="addPathParameters"/>), the object-axis scope
    /// parameters, the narrowing <c>@scope</c> / <c>@subject</c> /
    /// <c>@kind</c> filters, and the <c>@limit</c>; reads rows through
    /// <paramref name="readRow"/>; converts a <see cref="PostgresException"/>
    /// into a warning log + null return so the search falls back to
    /// freshness / zero-rank rather than failing the call. The "is there
    /// anything to search for" gate lives at the call sites — the lexical
    /// path's <see cref="MemoryFactLexical.TrySearchLexicalAsync"/> short-
    /// circuits on a blank query before opening a connection, and the
    /// vector path's null check on <c>query.Embedding</c> is in
    /// <see cref="EfMemoryStore.SearchAsync"/>.
    /// </summary>
    /// <param name="db">The context whose connection the command runs on.</param>
    /// <param name="searchSql">
    /// The path-specific ranked-search SQL — a constant from
    /// <see cref="MemoryFactSql"/>, never user input. The CA2100
    /// suppression below is justified because every caller passes one of
    /// the two SQL constants and there is no user-input path.
    /// </param>
    /// <param name="addPathParameters">
    /// Binds the path-specific parameters (vector + cutoff on the
    /// cosine path; <c>@lexicalQuery</c> on the lexical path) onto the
    /// command. Runs before the object-axis scope, narrowing filters,
    /// and limit are bound.
    /// </param>
    /// <param name="readRow">Reads one row from the open reader; called for every result row.</param>
    /// <param name="query">The query the path-specific parameters were derived from.</param>
    /// <param name="failureLogMessage">
    /// Format-string-friendly message for the warning log on
    /// <see cref="PostgresException"/>; the exception itself is the
    /// second log argument.
    /// </param>
    /// <param name="logger">Logger for the failure-path warning.</param>
    /// <param name="cancellationToken">Cancellation for the connection and command.</param>
    public static async Task<IReadOnlyList<T>?> ExecuteRankedAsync<T>(
        MemoryDbContext db,
        string searchSql,
        Action<System.Data.Common.DbCommand, MemoryFactQuery> addPathParameters,
        Func<System.Data.Common.DbDataReader, T> readRow,
        MemoryFactQuery query,
        string failureLogMessage,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                var connection = db.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // SQL injection — searchSql is always a constant from MemoryFactSql
                command.CommandText = searchSql;
#pragma warning restore CA2100
                addPathParameters(command, query);
                BindObjectAxisScope(db, command);
                BindNarrowingFilters(command, query);
                command.Parameters.Add(new NpgsqlParameter("limit", query.Limit));

                var rows = new List<T>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(readRow(reader));
                }

                return rows;
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (PostgresException exception)
        {
            // the probe said the column exists but the query disagrees
            // (migration drift) — the caller falls back to the other
            // ranker (vector → freshness, lexical → zero rank).
            logger.LogWarning(exception, "{Message}", failureLogMessage);
            return null;
        }
    }

    /// <summary>
    /// Object-axis scope parameters — these queries run raw SQL outside
    /// EF's model, so <see cref="MemoryDbContext"/>'s
    /// <c>HasQueryFilter</c> on <see cref="MemoryFact"/> cannot reach
    /// them; these two parameters reproduce the same rule directly (see
    /// <see cref="MemoryFactSql.CosineSearchSql"/> /
    /// <see cref="MemoryFactSql.LexicalRankSql"/>). Bound here exactly
    /// once per hybrid search.
    /// </summary>
    internal static void BindObjectAxisScope(MemoryDbContext db, System.Data.Common.DbCommand command)
    {
        command.Parameters.Add(new NpgsqlParameter("unrestricted", NpgsqlTypes.NpgsqlDbType.Boolean)
        {
            Value = db.ScopeUnrestricted,
        });
        command.Parameters.Add(new NpgsqlParameter("allowedProjectSubjectKeys", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = db.ScopeProjectSubjectKeys,
        });
    }

    /// <summary>
    /// Narrowing filter parameters — <c>@scope</c> / <c>@subject</c> /
    /// <c>@kind</c> widen on NULL; a typed null is required or the
    /// statement dies with 42P08.
    /// </summary>
    internal static void BindNarrowingFilters(System.Data.Common.DbCommand command, MemoryFactQuery query)
    {
        command.Parameters.Add(MemoryFactVectors.FilterTextParameter(
            "scope", query.Scope is { } scope ? MemoryScopeKeys.Key(scope) : null));
        command.Parameters.Add(MemoryFactVectors.FilterTextParameter(
            "subject", query.SubjectId is null ? null : MemoryFact.CanonicalKey(query.SubjectId)));
        command.Parameters.Add(MemoryFactVectors.FilterTextParameter(
            "kind", query.Kind is { } kind ? MemoryFactKindKeys.Key(kind) : null));
    }
}
