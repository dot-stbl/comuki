using System.ClientModel;
using Comuki.Host.Brain.Brain.Options;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Comuki.Host.Brain.Model;

/// <summary>
/// Builds the MEAI <see cref="IChatClient"/> over the configured
/// OpenAI-compatible endpoint (z.ai and friends; Anthropic models via the
/// provider's OpenAI-compat surface — the MEAI spike resolution). The
/// factory throws a setup hint when the model is unconfigured — surfaced
/// per brain call, so the host itself boots and sweeps without keys.
/// </summary>
public static class BrainChatClientFactory
{
    /// <summary>Creates the chat client for the configured leading model.</summary>
    /// <param name="model"></param>
    /// <exception cref="InvalidOperationException">The model options are incomplete.</exception>
    public static IChatClient Create(BrainModelOptions model)
    {
        if (!model.IsConfigured || model.Endpoint is not { } endpoint || model.ApiKey is not { } apiKey || model.ModelId is not { } modelId)
        {
            throw new InvalidOperationException(
                "brain model is not configured: set brain:model (endpoint/apiKey/modelId) or the "
                + $"{BrainOptions.ModelEndpointEnvVariable}/{BrainOptions.ModelApiKeyEnvVariable}/{BrainOptions.ModelIdEnvVariable} env vars");
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        return client.GetChatClient(modelId).AsIChatClient();
    }
}
