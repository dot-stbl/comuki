using Microsoft.Extensions.AI;

namespace Comuki.Host.Brain.Brain;

/// <summary>
/// Per-call chat client factory for the brain agent. The hot-reload
/// design (issue #53) builds a fresh <see cref="IChatClient"/> on every
/// invocation from the live <see cref="ModelConfig"/> — making the
/// factory an interface lets tests substitute a scripted client without
/// going through <c>BrainChatClientFactory.Create</c> (which constructs a
/// real <c>OpenAIClient</c>). Production wires the
/// <see cref="DefaultBrainChatClientFactory"/>; tests wire a fake.
/// </summary>
public interface IBrainChatClientFactory
{
    /// <summary>Builds the <see cref="IChatClient"/> for the resolved configuration.</summary>
    /// <param name="config">Live <see cref="ModelConfig"/> from <see cref="IModelConfigProvider"/>.</param>
    public IChatClient Create(ModelConfig config);
}
