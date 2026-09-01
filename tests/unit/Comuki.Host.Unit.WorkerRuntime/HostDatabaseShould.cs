using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// Unit tests for <see cref="HostDatabase"/> resolution: <c>COMUKI_DB</c>
/// wins over everything, the legacy <c>COMUKI_DATABASE</c> alias is
/// honored and flagged for the startup warning, <c>ConnectionStrings:Comuki</c>
/// is the last fallback, absence throws the setup hint, and the warning
/// fires only for the alias. One resolved value is what wires both host
/// halves — the worker runtime and identity/projects persistence.
/// </summary>
public sealed class HostDatabaseShould : IDisposable
{
    public HostDatabaseShould()
    {
        // Both env vars are process-wide; reset them per test so every
        // case exercises the resolution order from a clean slate.
        Environment.SetEnvironmentVariable(HostDatabase.EnvVariable, null);
        Environment.SetEnvironmentVariable(HostDatabase.LegacyEnvVariable, null);
    }

    [Fact(DisplayName = "Given COMUKI_DB with alias and config also set, when resolved, then COMUKI_DB wins")]
    public void PreferComukiDbOverAliasAndConfiguration()
    {
        Environment.SetEnvironmentVariable(HostDatabase.EnvVariable, "Host=from-comuki-db");
        Environment.SetEnvironmentVariable(HostDatabase.LegacyEnvVariable, "Host=from-alias");
        var configuration = BuildConfiguration("Host=from-config");

        var connection = HostDatabase.Resolve(configuration);

        connection.ConnectionString.ShouldBe("Host=from-comuki-db");
        connection.FromLegacyAlias.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given only the legacy COMUKI_DATABASE alias, when resolved, then it is honored and flagged")]
    public void HonorLegacyAliasAndFlagItForTheWarning()
    {
        Environment.SetEnvironmentVariable(HostDatabase.LegacyEnvVariable, "Host=from-alias");
        var configuration = BuildConfiguration("Host=from-config");

        var connection = HostDatabase.Resolve(configuration);

        connection.ConnectionString.ShouldBe("Host=from-alias");
        connection.FromLegacyAlias.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a blank COMUKI_DB and a set alias, when resolved, then the alias is honored")]
    public void SkipBlankComukiDbToTheAlias()
    {
        Environment.SetEnvironmentVariable(HostDatabase.EnvVariable, "  ");
        Environment.SetEnvironmentVariable(HostDatabase.LegacyEnvVariable, "Host=from-alias");
        var configuration = BuildConfiguration();

        var connection = HostDatabase.Resolve(configuration);

        connection.ConnectionString.ShouldBe("Host=from-alias");
        connection.FromLegacyAlias.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given no env vars, when resolved, then ConnectionStrings:Comuki is used")]
    public void FallBackToConfiguration()
    {
        var configuration = BuildConfiguration("Host=from-config");

        var connection = HostDatabase.Resolve(configuration);

        connection.ConnectionString.ShouldBe("Host=from-config");
        connection.FromLegacyAlias.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given no source at all, when resolved, then the setup hint throws")]
    public void ThrowTheSetupHintWhenNothingIsSet()
    {
        var configuration = BuildConfiguration();

        var exception = Should.Throw<InvalidOperationException>(() => HostDatabase.Resolve(configuration));

        exception.Message.ShouldBe(
            $"connection string not found: set the {HostDatabase.EnvVariable} env var "
            + $"or ConnectionStrings:{HostDatabase.ConnectionStringName} in configuration");
    }

    [Fact(DisplayName = "Given a legacy-alias connection, when WarnLegacyAlias runs, then one warning names both env vars")]
    public void WarnOnceForTheLegacyAlias()
    {
        var logger = new RecordingLogger();
        var connection = new HostDatabase.Connection("Host=from-alias", FromLegacyAlias: true);

        HostDatabase.WarnLegacyAlias(connection, logger);

        var (level, message) = logger.Entries.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        message.ShouldContain(HostDatabase.LegacyEnvVariable);
        message.ShouldContain(HostDatabase.EnvVariable);
    }

    [Fact(DisplayName = "Given a COMUKI_DB connection, when WarnLegacyAlias runs, then nothing is logged")]
    public void StaySilentWithoutTheAlias()
    {
        var logger = new RecordingLogger();
        var connection = new HostDatabase.Connection("Host=from-comuki-db", FromLegacyAlias: false);

        HostDatabase.WarnLegacyAlias(connection, logger);

        logger.Entries.ShouldBeEmpty();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(HostDatabase.EnvVariable, null);
        Environment.SetEnvironmentVariable(HostDatabase.LegacyEnvVariable, null);
    }

    private static IConfigurationRoot BuildConfiguration(string? connectionString = null)
    {
        var values = new Dictionary<string, string?>();
        if (connectionString is not null)
        {
            values[$"ConnectionStrings:{HostDatabase.ConnectionStringName}"] = connectionString;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
