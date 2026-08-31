namespace Comuki.Shared.Contracts.ControlPlane.ChatCommands;

/// <summary>
/// Port to the built-in chat-command pack: the control-plane
/// <c>chat-commands/</c> folder. The chat harness merges these with custom
/// commands from the client git.
/// </summary>
public interface IChatCommandCatalog
{
    /// <summary>Every valid built-in command, ordered by key. Malformed documents are skipped with a warning, not fatal.</summary>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ChatCommandDefinition>> ListCommandsAsync(CancellationToken cancellationToken = default);
}
