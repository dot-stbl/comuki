using System.Data.Common;

namespace Comuki.Shared.Kernel.Persistence;

/// <summary>
/// Shared ADO plumbing for raw <see cref="DbCommand"/>-based queries that
/// the engine's and the host's guarded-SQL sites both reach for. Keeps the
/// three repeated shapes — typed parameter binding, the
/// <c>name + value</c> record that feeds it, and the
/// <see cref="DbCommand"/> factory — in one file so a new caller doesn't
/// reinvent them, and so a CA2100 / SQL-injection review or a future
/// change to parameter binding lives in one place. Used by
/// <c>WorkItemQueueSql</c>, <c>RunCancelSql</c>, <c>OutboxDispatchSql</c>,
/// <c>InboxSql</c>, <c>EscalationTimeoutSql</c> and <c>SteerSql</c>; new
/// call sites should not duplicate these helpers.
/// </summary>
internal static class AdoCommandHelpers
{
    /// <summary>
    /// Typed SQL parameter pair — feeds the <see cref="CreateCommand"/> params array.
    /// </summary>
    /// <param name="Name">Parameter name with the <c>@</c> prefix.</param>
    /// <param name="Value">CLR value to bind; Npgsql infers uuid / timestamptz / text from the type.</param>
    public sealed record CommandParam(string Name, object Value);

    /// <summary>
    /// Creates a prepared command on the transaction's connection, binds the supplied
    /// typed parameters, and returns it. Callers that have multiple parameters for a
    /// single statement collapse to one <c>CreateCommand(transaction, sql, params)</c>
    /// call instead of re-stating the connection / command / parameter dance.
    /// </summary>
    /// <param name="transaction">Open transaction whose connection the command runs on.</param>
    /// <param name="sql">Parameterised SQL to execute.</param>
    /// <param name="parameters">Typed parameters in the order they appear in <paramref name="sql"/>.</param>
    public static DbCommand CreateCommand(DbTransaction transaction, string sql, params CommandParam[] parameters)
    {
        // boundary: ADO contract — Connection is always set on a live transaction.
        // Sql is always a hardcoded constant from the caller's class — never user input.
#pragma warning disable CA2100 // Review SQL injection. See comment above.
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = sql;
#pragma warning restore CA2100
        foreach (var parameter in parameters)
        {
            AddParameter(command, parameter.Name, parameter.Value);
        }

        return command;
    }

    /// <summary>
    /// Adds one typed parameter (Npgsql infers uuid / timestamptz / text from the
    /// CLR value). Used directly by call sites that don't pass through
    /// <see cref="CreateCommand"/> — typically single-parameter probes.
    /// </summary>
    /// <param name="command">Command the parameter is added to.</param>
    /// <param name="name">Parameter name including the <c>@</c> prefix.</param>
    /// <param name="value">Parameter value (uuid / text / timestamptz / int).</param>
    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
