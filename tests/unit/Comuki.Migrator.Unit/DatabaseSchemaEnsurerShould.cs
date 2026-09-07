using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Migrator.Unit;

/// <summary>
/// Schema-dispatch contract: every module schema gets
/// <c>CREATE SCHEMA IF NOT EXISTS</c> exactly as its switch arm prescribes,
/// second runs are no-ops, unknown names fail fast with <c>ArgumentException</c>,
/// and bad connection strings surface through the <see cref="NpgsqlException"/>
/// hierarchy. Each test reuses a single Testcontainers-managed Postgres
/// instance bootstrapped in <see cref="InitializeAsync"/>.
/// </summary>
public sealed class DatabaseSchemaEnsurerShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given a schema name outside the eight-module whitelist, when EnsureAsync runs, then throws ArgumentException without touching the connection")]
    public async Task RejectsUnknownSchemaAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<ArgumentException>(
            async () => await DatabaseSchemaEnsurer.EnsureAsync(
                container.GetConnectionString(),
                "not-a-real-schema",
                cancellationToken));
    }

    [Fact(DisplayName = "Given an unreachable host, when EnsureAsync runs, then Npgsql surfaces the connection failure")]
    public async Task SurfacesConnectionFailureAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string badConnection =
            "Host=does-not-exist.invalid;Database=comuki;Username=comuki;Password=comuki;Timeout=2;Command Timeout=2";

        await Should.ThrowAsync<NpgsqlException>(
            async () => await DatabaseSchemaEnsurer.EnsureAsync(
                badConnection,
                OrchestrationDatabase.Schema,
                cancellationToken));
    }

    [Theory(DisplayName = "Given schema {0}, when EnsureAsync runs twice, then the second call is a no-op and the schema is visible in information_schema.schemata")]
    [MemberData(nameof(PostgresHelpers.KnownSchemas), MemberType = typeof(PostgresHelpers))]
    public async Task EnsureIsIdempotentAsync(string schema)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = container.GetConnectionString();

        await DatabaseSchemaEnsurer.EnsureAsync(connectionString, schema, cancellationToken);
        await DatabaseSchemaEnsurer.EnsureAsync(connectionString, schema, cancellationToken);

        var schemas = await PostgresHelpers.QuerySchemasAsync(connectionString, cancellationToken);
        schemas.ShouldContain(schema);
    }

    [Fact(DisplayName = "Given all eight module schemas in sequence, when EnsureAsync runs once each, then every schema is present in information_schema.schemata")]
    public async Task CreatesAllEightSchemasAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = container.GetConnectionString();

        foreach (var schema in PostgresHelpers.AllSchemas())
        {
            await DatabaseSchemaEnsurer.EnsureAsync(connectionString, schema, cancellationToken);
        }

        var schemas = await PostgresHelpers.QuerySchemasAsync(connectionString, cancellationToken);
        foreach (var schema in PostgresHelpers.AllSchemas())
        {
            schemas.ShouldContain(schema);
        }
    }
}
