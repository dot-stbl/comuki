using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// Raw ADO surface for the pgvector embedding column: attach + the
/// availability probe + the cosine search. SQL text lives in
/// <see cref="MemoryFactSql"/>; parameters are always bound, never
/// interpolated. Shares <see cref="MemoryFactHybridSearch.ColumnExistsAsync"/>
/// and <see cref="MemoryFactHybridSearch.ExecuteRankedAsync{T}"/> with
/// the lexical path — the two searches differ only in their
/// path-specific parameters (vector + cutoff vs lexical query text) and
/// in the trailing rank column they read.
/// </summary>
internal static class MemoryFactVectors
{
    /// <summary>
    /// Writes the embedding for one fact row inside the caller's write
    /// transaction — the command joins the transaction's open connection,
    /// so a crash rolls the fact and its vector back together.
    /// </summary>
    /// <param name="db">The context whose connection the command runs on.</param>
    /// <param name="id">Fact row whose embedding to write.</param>
    /// <param name="vector">The pgvector-formatted embedding bytes.</param>
    /// <param name="cancellationToken">Cancellation for the embedded execute.</param>
    public static async Task AttachAsync(
        MemoryDbContext db,
        MemoryFactId id,
        float[] vector,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = MemoryFactSql.UpdateEmbeddingSql;
        command.Parameters.Add(new NpgsqlParameter("id", id.Value));
        command.Parameters.Add(VectorParameter("vector", vector));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Asks whether the <c>embedding</c> column exists on the memory_facts table.</summary>
    /// <param name="db">The context whose connection the probe runs on.</param>
    /// <param name="cancellationToken">Cancellation for the probe.</param>
    public static Task<bool> HasColumnAsync(MemoryDbContext db, CancellationToken cancellationToken)
    {
        return MemoryFactHybridSearch.ColumnExistsAsync(db, MemoryFactSql.EmbeddingColumnExistsSql, cancellationToken);
    }

    /// <summary>
    /// Runs the cosine-distance path against the pgvector column. Falls
    /// back to freshness / zero-rank on a probe-stale <c>embedding</c>
    /// column — the runner logs the failure and returns <c>null</c>.
    /// </summary>
    /// <param name="db">The context whose connection the command runs on.</param>
    /// <param name="embedding">Vector to search for; serialised via <see cref="MemoryFactSql.VectorLiteral"/>.</param>
    /// <param name="query">Query scope / subject / kind / limit narrowing filters.</param>
    /// <param name="ephemeralCutoff">Facts whose <c>kind = ephemeral</c> and whose <c>created_at</c> predates this are skipped.</param>
    /// <param name="logger">Logger for the failure-path warning.</param>
    /// <param name="cancellationToken">Cancellation for the connection and command.</param>
    public static async Task<IReadOnlyList<MemoryFactView>?> TrySearchCosineAsync(
        MemoryDbContext db,
        float[] embedding,
        MemoryFactQuery query,
        DateTimeOffset ephemeralCutoff,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        return await MemoryFactHybridSearch.ExecuteRankedAsync(
            db,
            MemoryFactSql.CosineSearchSql,
            (command, q) =>
            {
                command.Parameters.Add(VectorParameter("vector", embedding));
                command.Parameters.Add(new NpgsqlParameter("cutoff", ephemeralCutoff));
            },
            static reader => MemoryFactSql.ReadView(reader),
            query,
            "cosine search failed; falling back to freshness ranking",
            logger,
            cancellationToken);
    }

    /// <summary>
    /// Builds the <c>@vector</c> parameter. Unknown-typed: PostgreSQL
    /// treats the value as an untyped literal and the explicit
    /// <c>::vector</c> cast in the SQL types it (a bare unknown
    /// parameter in the operator position fails with 42P08). An
    /// explicitly text-typed parameter has no cast to vector and kills
    /// the statement.
    /// </summary>
    /// <param name="name">The SQL parameter name (with the <c>@</c> prefix).</param>
    /// <param name="vector">The vector to bind; serialised via <see cref="MemoryFactSql.VectorLiteral"/>.</param>
    public static NpgsqlParameter VectorParameter(string name, float[] vector)
    {
        return new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Unknown)
        {
            Value = MemoryFactSql.VectorLiteral(vector),
        };
    }

    /// <summary>
    /// Builds a typed text parameter. The <c>@param IS NULL</c> filter
    /// narrowing needs a typed null — an untyped <c>DBNull</c>
    /// parameter fails with 42P08 (data type undetermined).
    /// </summary>
    /// <param name="name">The SQL parameter name (with the <c>@</c> prefix).</param>
    /// <param name="value">String value; <c>null</c> is bound as <c>DBNull</c>.</param>
    public static NpgsqlParameter FilterTextParameter(string name, string? value)
    {
        return new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = value is null ? DBNull.Value : value,
        };
    }
}
