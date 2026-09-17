using System.ClientModel;
using Comuki.Host.Brain.Brain.Exceptions;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Comuki.Host.Brain.Brain;

/// <summary>
/// Production <see cref="IBrainChatClientFactory"/>: builds the MEAI
/// <see cref="IChatClient"/> over the resolved model configuration
/// (z.ai and friends; Anthropic models via the provider's
/// OpenAI-compat surface). The agent loop calls
/// <see cref="Create"/> on every invocation so a Vault / Consul
/// rotation lands within the resolver's TTL (60s default) without a
/// host restart.
/// </summary>
public sealed class DefaultBrainChatClientFactory : IBrainChatClientFactory
{
    /// <inheritdoc />
    public IChatClient Create(ModelConfig config)
    {
        return BrainChatClientBuildHelpers.TryBuild(config) is { } client
            ? client
            : throw new BrainModelNotConfiguredException(
                "brain model is not configured: every ModelConfig field (Endpoint/ApiKey/ModelId) must be set; "
                + "check that ModelConfigProvider.ResolveAsync produced a complete configuration");
    }
}

/// <summary>
/// Pure helpers for <see cref="DefaultBrainChatClientFactory"/>:
/// validation + OpenAI client construction, isolated so the production
/// class stays free of private business logic (<c>code-shape.md</c> §1a).
/// </summary>
file static class BrainChatClientBuildHelpers
{
    /// <summary>Builds the chat client when every field is set, null otherwise.</summary>
    /// <param name="config"></param>
    public static IChatClient? TryBuild(ModelConfig config)
    {
        return IsComplete(config.Endpoint, config.ApiKey, config.ModelId)
            ? Build(config.Endpoint, config.ApiKey, config.ModelId)
            : null;
    }

    /// <summary>True when every required connection field is a non-empty string.</summary>
    public static bool IsComplete(string? endpoint, string? apiKey, string? modelId)
    {
        return !string.IsNullOrWhiteSpace(endpoint)
            && !string.IsNullOrWhiteSpace(apiKey)
            && !string.IsNullOrWhiteSpace(modelId);
    }

    /// <summary>Builds the <see cref="IChatClient"/> over the OpenAI-compatible endpoint.</summary>
    public static IChatClient Build(string endpoint, string apiKey, string modelId)
    {
        return new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetChatClient(modelId)
            .AsIChatClient();
    }
}
