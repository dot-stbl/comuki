using System.Data.Common;
using Comuki.Engine.Orchestration.Domain;

namespace Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;

/// <summary>
/// Guarded raw SQL + ADO plumbing for <see cref="EscalationTimeoutSweeper"/>.
/// One statement transitions every stale Escalated run to Cancelled and
/// returns the archived rows. The guard (<c>status = 'Escalated'</c>) is
/// baked into the WHERE — concurrent operators who re-queue a row between
/// the sweep's open and the UPDATE lose the race safely: their update
/// changes the status and our matching predicate no longer matches, so
/// the row is preserved as-is. Status literals are sourced from
/// <see cref="RunStatus"/> via <c>nameof</c> so a rename fails the build
/// instead of leaving this predicate silently stale.
/// </summary>
internal static class EscalationTimeoutSql
{
    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string Escalated = nameof(RunStatus.Escalated);

    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string Cancelled = nameof(RunStatus.Cancelled);

    /// <summary>
    /// Single-statement archive: transitions every run whose status is
    /// still <c>Escalated</c> and whose <c>updated_at</c> is older than
    /// the cutoff to <c>Cancelled</c>, stamps <c>updated_at = @now</c>,
    /// and returns the affected rows so the caller can build the journal
    /// events in one SaveChanges.
    /// </summary>
    public const string ArchiveSql =
        "UPDATE " + Persistence.OrchestrationDatabase.Schema + "." + Persistence.OrchestrationDatabase.Runs + " "
        + "SET status = '" + Cancelled + "', updated_at = @now "
        + "WHERE status = '" + Escalated + "' AND updated_at < @cutoff "
        + "RETURNING id, updated_at";

    /// <summary>Builds the prepared archive command on the transaction's connection.</summary>
    public static DbCommand CreateArchiveCommand(
        DbTransaction transaction,
        DateTimeOffset cutoff,
        DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = ArchiveSql;
        AddParameter(command, "@cutoff", cutoff);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Adds one typed parameter (Npgsql infers the CLR type from the value).</summary>
    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
