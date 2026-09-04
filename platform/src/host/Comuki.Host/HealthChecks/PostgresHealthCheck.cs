using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Comuki.Host.HealthChecks;

/// <summary>
/// Probes the orchestrator Postgres database with a 2-second timeout.
/// Runs the trivial <c>SELECT 1</c> round-trip so the connection string,
/// credentials and TLS settings are exercised end-to-end.
/// </summary>
/// <param name="connectionString">Postgres connection string (the same one the host uses).</param>
public sealed class PostgresHealthCheck(string connectionString) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 2;
            _ = await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy(description: "Postgres SELECT 1 returned 1");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(description: "Postgres probe failed", exception: ex);
        }
    }
}
