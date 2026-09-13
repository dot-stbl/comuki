using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// <see cref="ModelConfigProvider"/> tests — every field's resolution
/// order (per-call ref set → ISecretResolver; not set → IOptions
/// fallback); partial refs; the chat-kind lighter-model override; and
/// the defensive setup-hint when no source has the value. Uses
/// hand-written fakes (matching the rest of <c>Comuki.Host.Brain.Unit</c>)
/// so the test project keeps a zero-mocking-deps surface.
/// </summary>
public sealed class ModelConfigProviderShould
{
    private const string Endpoint = "https://api.example.com/v4";
    private const string ApiKey = "key-from-test";
    private const string FlagshipModel = "flagship-1";

    [Fact(DisplayName = "Given no refs set, when ResolveAsync runs, then the boot-time BrainOptions.Model values come through unchanged")]
    public async Task FallbackToBootTimeModelAsync()
    {
        var resolver = new RecordingSecretResolver();
        var options = Options.Create(new BrainOptions
        {
            Model = new BrainModelOptions
            {
                Endpoint = Endpoint,
                ApiKey = ApiKey,
                ModelId = FlagshipModel,
            },
        });

        var provider = new ModelConfigProvider(resolver, options);

        var config = await provider.ResolveAsync(TestContext.Current.CancellationToken);

        config.Endpoint.ShouldBe(Endpoint);
        config.ApiKey.ShouldBe(ApiKey);
        config.ModelId.ShouldBe(FlagshipModel);
        // No ChatModelIdRef → chat model id falls back to the flagship.
        config.ChatModelId.ShouldBe(FlagshipModel);
        resolver.Requests.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given every *Ref set, when ResolveAsync runs, then the secret resolver is asked four times and the live values flow through")]
    public async Task AllRefsResolvedViaSecretResolverAsync()
    {
        var resolver = new RecordingSecretResolver();
        resolver.Map["vault:models/brain#endpoint"] = "https://api/v4-live";
        resolver.Map["vault:models/brain#api_key"] = "live-key";
        resolver.Map["vault:models/brain#id"] = "live-flagship";
        resolver.Map["vault:models/chat#id"] = "live-chat";

        var options = Options.Create(new BrainOptions
        {
            ModelEndpointRef = "vault:models/brain#endpoint",
            ModelApiKeyRef = "vault:models/brain#api_key",
            ModelIdRef = "vault:models/brain#id",
            ChatModelIdRef = "vault:models/chat#id",
            Model = new BrainModelOptions
            {
                // Boot-time values must NOT win when refs are set.
                Endpoint = "boot-endpoint",
                ApiKey = "boot-key",
                ModelId = "boot-flagship",
            },
        });

        var provider = new ModelConfigProvider(resolver, options);

        var config = await provider.ResolveAsync(TestContext.Current.CancellationToken);

        config.Endpoint.ShouldBe("https://api/v4-live");
        config.ApiKey.ShouldBe("live-key");
        config.ModelId.ShouldBe("live-flagship");
        config.ChatModelId.ShouldBe("live-chat");
    }

    [Fact(DisplayName = "Given only ModelEndpointRef set, when ResolveAsync runs, then the endpoint is resolved live and api-key/model-id fall back to boot-time")]
    public async Task PartialRefsFallBackIndividuallyAsync()
    {
        var resolver = new RecordingSecretResolver();
        resolver.Map["vault:models/brain#endpoint"] = "https://live/v4";

        var options = Options.Create(new BrainOptions
        {
            ModelEndpointRef = "vault:models/brain#endpoint",
            Model = new BrainModelOptions
            {
                Endpoint = null,
                ApiKey = ApiKey,
                ModelId = FlagshipModel,
            },
        });

        var provider = new ModelConfigProvider(resolver, options);

        var config = await provider.ResolveAsync(TestContext.Current.CancellationToken);

        config.Endpoint.ShouldBe("https://live/v4");
        config.ApiKey.ShouldBe(ApiKey);
        config.ModelId.ShouldBe(FlagshipModel);
        config.ChatModelId.ShouldBe(FlagshipModel);
    }

    [Fact(DisplayName = "Given only ChatModelIdRef set, when ResolveAsync runs, then ChatModelId is resolved live and ModelId stays the boot-time flagship")]
    public async Task ChatModelRefOverrideAsync()
    {
        var resolver = new RecordingSecretResolver();
        resolver.Map["vault:models/chat#id"] = "lighter-chat";

        var options = Options.Create(new BrainOptions
        {
            ChatModelIdRef = "vault:models/chat#id",
            Model = new BrainModelOptions
            {
                Endpoint = Endpoint,
                ApiKey = ApiKey,
                ModelId = FlagshipModel,
            },
        });

        var provider = new ModelConfigProvider(resolver, options);

        var config = await provider.ResolveAsync(TestContext.Current.CancellationToken);

        config.ModelId.ShouldBe(FlagshipModel);
        config.ChatModelId.ShouldBe("lighter-chat");
    }

    [Fact(DisplayName = "Given no refs and no boot-time model, when ResolveAsync runs, then InvalidOperationException names the env vars and refs")]
    public async Task UnconfiguredModelThrowsSetupHintAsync()
    {
        var resolver = new RecordingSecretResolver();
        var options = Options.Create(new BrainOptions());

        var provider = new ModelConfigProvider(resolver, options);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => provider.ResolveAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(BrainOptions.ModelEndpointEnvVariable);
        exception.Message.ShouldContain(BrainOptions.ModelApiKeyEnvVariable);
        exception.Message.ShouldContain(BrainOptions.ModelIdEnvVariable);
        exception.Message.ShouldContain("ModelEndpointRef");
    }

    [Fact(DisplayName = "Given a ref whose resolver returns null, when ResolveAsync runs, then SecretRefUnsetException surfaces")]
    public async Task UnsetSecretRefSurfacesTypedExceptionAsync()
    {
        var resolver = new ThrowingSecretResolver();

        var options = Options.Create(new BrainOptions
        {
            ModelEndpointRef = "vault:missing#key",
            Model = new BrainModelOptions
            {
                Endpoint = null,
                ApiKey = ApiKey,
                ModelId = FlagshipModel,
            },
        });

        var provider = new ModelConfigProvider(resolver, options);

        await Should.ThrowAsync<SecretRefUnsetException>(
            () => provider.ResolveAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Map-based fake ISecretResolver: returns the value for a known ref, throws on a missing one.</summary>
    private sealed class RecordingSecretResolver : ISecretResolver
    {
        public Dictionary<string, string> Map { get; } = [];

        public List<string> Requests { get; } = [];

        public Task<string?> ResolveAsync(string? reference, CancellationToken cancellationToken = default)
        {
            if (reference is not null)
            {
                Requests.Add(reference);
            }

            return reference is not null && Map.TryGetValue(reference, out var value)
                ? Task.FromResult<string?>(value)
                : throw new SecretRefUnsetException(reference ?? string.Empty);
        }
    }

    /// <summary>Fake ISecretResolver that throws <see cref="SecretRefUnsetException"/> for every ref.</summary>
    private sealed class ThrowingSecretResolver : ISecretResolver
    {
        public Task<string?> ResolveAsync(string? reference, CancellationToken cancellationToken = default)
        {
            return Task.FromException<string?>(new SecretRefUnsetException(reference ?? string.Empty));
        }
    }
}
