namespace Comuki.Host.Brain.Brain;

/// <summary>
/// Effective model configuration for a single brain call. The
/// <see cref="Endpoint"/>, <see cref="ApiKey"/> and
/// <see cref="ModelId"/> fields drive the OpenAI-compatible chat
/// client the agent builds per call. <see cref="ChatModelId"/> is the
/// lighter model the chat graph asks for when
/// <c>BrainOptions.ChatModelIdRef</c> is set (the default model
/// otherwise — see <see cref="ModelConfigProvider"/>).
/// </summary>
/// <param name="Endpoint">OpenAI-compatible base endpoint.</param>
/// <param name="ApiKey">API key for the upstream.</param>
/// <param name="ModelId">Flagship model id (plan / brief / repair / default).</param>
/// <param name="ChatModelId">Lighter model id used for chat-kind requests.</param>
public sealed record ModelConfig(string Endpoint, string ApiKey, string ModelId, string ChatModelId);
