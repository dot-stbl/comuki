using Comuki.Host.Brain.Brain.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    [Fact(DisplayName = "Given the *Ref config keys set, when Resolve runs, then each *Ref binds to the matching option")]
    public void BindRefProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["brain:ModelEndpointRef"] = "vault:models/brain#endpoint",
                ["brain:ModelApiKeyRef"] = "vault:models/brain#api_key",
                ["brain:ModelIdRef"] = "vault:models/brain#id",
                ["brain:ChatModelIdRef"] = "vault:models/chat#id",
            })
            .Build();

        var options = BrainOptions.Resolve(configuration);

        options.ModelEndpointRef.ShouldBe("vault:models/brain#endpoint");
        options.ModelApiKeyRef.ShouldBe("vault:models/brain#api_key");
        options.ModelIdRef.ShouldBe("vault:models/brain#id");
        options.ChatModelIdRef.ShouldBe("vault:models/chat#id");
    }

    [Fact(DisplayName = "Given *Ref keys absent, when Resolve runs, then the *Ref properties stay null and the boot-time model fallback is untouched")]
    public void RefPropertiesStayNullWhenUnset()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["brain:model:Endpoint"] = "https://api.example.com/v4",
                ["brain:model:ApiKey"] = "key",
                ["brain:model:ModelId"] = "glm-5",
            })
            .Build();

        var options = BrainOptions.Resolve(configuration);

        options.ModelEndpointRef.ShouldBeNull();
        options.ModelApiKeyRef.ShouldBeNull();
        options.ModelIdRef.ShouldBeNull();
        options.ChatModelIdRef.ShouldBeNull();
        options.Model.IsConfigured.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a whitespace *Ref value, when Resolve runs, then it normalises to null so the boot-time fallback wins")]
    public void WhitespaceRefNormalisesToNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["brain:ModelEndpointRef"] = "   ",
                ["brain:model:Endpoint"] = "https://boot.example.com/v4",
            })
            .Build();

        var options = BrainOptions.Resolve(configuration);

        options.ModelEndpointRef.ShouldBeNull();
        options.Model.Endpoint.ShouldBe("https://boot.example.com/v4");
    }

    [Fact(DisplayName = "Given AddBrainOptions wiring, when the provider resolves IOptions<BrainOptions>, then consumers see the resolved bound values, not an unbound default")]
    public void OptionsRegistrationServesBoundValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["brain:GrpcPort"] = "17014",
                ["brain:MaxToolIterations"] = "4",
                ["brain:model:Endpoint"] = "https://api.example.com/v4",
                ["brain:ModelEndpointRef"] = "vault:models/brain#endpoint",
            })
            .Build();
        var resolved = BrainOptions.Resolve(configuration);
        var services = new ServiceCollection();
        services.AddBrainOptions(configuration, resolved);

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<BrainOptions>>().Value;

        options.GrpcPort.ShouldBe(17014);
        options.MaxToolIterations.ShouldBe(4);
        options.Model.Endpoint.ShouldBe("https://api.example.com/v4");
        options.ModelEndpointRef.ShouldBe("vault:models/brain#endpoint");
    }

    [Fact(DisplayName = "Given model creds only in COMUKI_ env vars (absent from IConfiguration), when IOptions<BrainOptions> resolves, then consumers see the resolved instance with the env fallback")]
    public void OptionsRegistrationServesResolvedInstanceOverFactoryBound()
    {
        // The env var is deliberately NOT mapped into IConfiguration: a
        // factory-bound instance would leave Model.Endpoint null, only
        // the pinned Resolve output (env fallback applied) carries it.
        Environment.SetEnvironmentVariable(BrainOptions.ModelEndpointEnvVariable, "https://env-fallback.example.com/v4");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["brain:GrpcPort"] = "17014",
                })
                .Build();
            var resolved = BrainOptions.Resolve(configuration);
            var services = new ServiceCollection();
            services.AddBrainOptions(configuration, resolved);

            var options = services.BuildServiceProvider()
                .GetRequiredService<IOptions<BrainOptions>>().Value;

            options.GrpcPort.ShouldBe(17014);
            options.Model.Endpoint.ShouldBe("https://env-fallback.example.com/v4");
        }
        finally
        {
            Environment.SetEnvironmentVariable(BrainOptions.ModelEndpointEnvVariable, null);
        }
    }

    [Fact(DisplayName = "Given a GrpcPort outside the port pool, when the options pipeline materialises, then the DataAnnotations Range gate rejects it")]
    public void OptionsRegistrationEnforcesRangeValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["brain:GrpcPort"] = "80",
            })
            .Build();
        var resolved = BrainOptions.Resolve(configuration);
        var services = new ServiceCollection();
        services.AddBrainOptions(configuration, resolved);

        // Consumers resolve the pinned resolved instance; the pipeline
        // (what ValidateOnStart runs at boot) rejects the out-of-range
        // port through IOptionsMonitor.
        Should.Throw<OptionsValidationException>(() =>
            _ = services.BuildServiceProvider()
                .GetRequiredService<IOptionsMonitor<BrainOptions>>().CurrentValue);
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
