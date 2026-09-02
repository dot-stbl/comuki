using Comuki.Host.Brain.Brain.Options;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// Host option resolution: config section first, model env vars filling
/// the gaps; an unconfigured model keeps the host bootable; the database
/// connection resolves from env or configuration with a setup hint
/// otherwise.
/// </summary>
public sealed class BrainHostOptionsShould
{
    [Fact(DisplayName = "Given a full config section, when Resolve runs, then every value binds")]
    public void BindConfigSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["brain:GrpcPort"] = "17014",
                ["brain:MaxToolIterations"] = "4",
                ["brain:model:Endpoint"] = "https://api.example.com/v4",
                ["brain:model:ApiKey"] = "key-from-config",
                ["brain:model:ModelId"] = "glm-5",
            })
            .Build();

        var options = BrainOptions.Resolve(configuration);

        options.GrpcPort.ShouldBe(17014);
        options.MaxToolIterations.ShouldBe(4);
        options.Model.Endpoint.ShouldBe("https://api.example.com/v4");
        options.Model.ApiKey.ShouldBe("key-from-config");
        options.Model.ModelId.ShouldBe("glm-5");
        options.Model.IsConfigured.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given defaults only, when Resolve runs, then the port pool default and cap apply and the model stays unconfigured")]
    public void FallBackToDefaults()
    {
        var options = BrainOptions.Resolve(new ConfigurationBuilder().Build());

        options.GrpcPort.ShouldBe(BrainOptions.DefaultGrpcPort);
        options.MaxToolIterations.ShouldBe(BrainOptions.DefaultMaxToolIterations);
        options.Model.IsConfigured.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given env vars and an empty config section, when Resolve runs, then env fills the model gaps")]
    public void FillModelFromEnvironment()
    {
        SetEnvironment(
            (BrainOptions.ModelEndpointEnvVariable, "https://env.example.com/v4"),
            (BrainOptions.ModelApiKeyEnvVariable, "key-from-env"),
            (BrainOptions.ModelIdEnvVariable, "env-model"));
        try
        {
            var options = BrainOptions.Resolve(new ConfigurationBuilder().Build());

            options.Model.IsConfigured.ShouldBeTrue();
            options.Model.Endpoint.ShouldBe("https://env.example.com/v4");
        }
        finally
        {
            RestoreEnvironment();
        }
    }

    [Fact(DisplayName = "Given config beats env, when Resolve runs, then the config endpoint wins")]
    public void PreferConfigOverEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["brain:model:Endpoint"] = "https://config.example.com/v4",
            })
            .Build();
        SetEnvironment((BrainOptions.ModelEndpointEnvVariable, "https://env.example.com/v4"));
        try
        {
            var options = BrainOptions.Resolve(configuration);

            options.Model.Endpoint.ShouldBe("https://config.example.com/v4");
        }
        finally
        {
            RestoreEnvironment();
        }
    }

    private static void SetEnvironment(params (string Name, string? Value)[] variables)
    {
        foreach (var (name, value) in variables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static void RestoreEnvironment()
    {
        SetEnvironment(
            (BrainOptions.ModelEndpointEnvVariable, null),
            (BrainOptions.ModelApiKeyEnvVariable, null),
            (BrainOptions.ModelIdEnvVariable, null));
    }
}
