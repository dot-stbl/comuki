using Microsoft.EntityFrameworkCore;

namespace Comuki.Shared.Migrations.Targets;

/// <summary>
/// One module migration step: creates the module context, ensures its
/// schema exists, then applies pending migrations. <see cref="PendingAsync"/>
/// is the status-mode twin — same discovery, no writes.
/// </summary>
/// <param name="label">The schema label used in status and apply reporting.</param>
/// <param name="schema">The module's schema name (a <c>&lt;Module&gt;Database.Schema</c> const).</param>
/// <param name="createContext">Builds the module <c>DbContext</c> over a connection string.</param>
public sealed class MigrationTarget(string label, string schema, Func<string, DbContext> createContext)
{
    /// <summary>The schema label used in status and apply reporting.</summary>
    public string Label => label;

    /// <summary>The module's schema name (a <c>&lt;Module&gt;Database.Schema</c> const).</summary>
    public string Schema => schema;

    /// <summary>Ensures the schema and applies pending migrations; returns the migrations that were applied.</summary>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public async Task<IReadOnlyList<string>> ApplyAsync(string connectionString, CancellationToken cancellationToken)
    {
        var context = createContext(connectionString);
        await using (context)
        {
            await DatabaseSchemaEnsurer.EnsureAsync(connectionString, schema, cancellationToken);

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await context.Database.MigrateAsync(cancellationToken);

            return pending;
        }
    }

    /// <summary>Pending migrations of the schema without applying anything and without any DDL.</summary>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public async Task<IReadOnlyList<string>> PendingAsync(string connectionString, CancellationToken cancellationToken)
    {
        var context = createContext(connectionString);
        await using (context)
        {
            return [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];
        }
    }
}
