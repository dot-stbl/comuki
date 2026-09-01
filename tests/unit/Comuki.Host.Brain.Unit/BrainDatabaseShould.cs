using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// Connection-string resolution order for the brain host: env var first,
/// configuration second, setup-hint exception otherwise.
/// </summary>
public sealed class BrainDatabaseShould
{
    [Fact(DisplayName = "Given the env var, when Resolve runs, then it wins")]
    public void PreferEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable(BrainDatabase.EnvVariable, "Host=env-host");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Comuki"] = "Host=config-host",
                })
                .Build();

            BrainDatabase.Resolve(configuration).ShouldBe("Host=env-host");
        }
        finally
        {
            Environment.SetEnvironmentVariable(BrainDatabase.EnvVariable, null);
        }
    }

    [Fact(DisplayName = "Given only configuration, when Resolve runs, then the connection string section is read")]
    public void ReadFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Comuki"] = "Host=config-host",
            })
            .Build();

        BrainDatabase.Resolve(configuration).ShouldBe("Host=config-host");
    }

    [Fact(DisplayName = "Given neither source, when Resolve runs, then the setup hint names both")]
    public void ThrowSetupHint()
    {
        Environment.SetEnvironmentVariable(BrainDatabase.EnvVariable, null);
        var configuration = new ConfigurationBuilder().Build();

        var exception = Should.Throw<InvalidOperationException>(() => BrainDatabase.Resolve(configuration));

        exception.Message.ShouldContain(BrainDatabase.EnvVariable);
        exception.Message.ShouldContain("ConnectionStrings:Comuki");
    }
}
