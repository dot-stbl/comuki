using Comuki.Shared.Contracts.ControlPlane.ChatCommands;

namespace Comuki.Modules.Chat.Application.Slash;

/// <summary>
/// The merged slash-command catalog: graph-native built-ins plus the
/// control-plane <c>chat-commands/</c> pack. Built-ins win on key collision
/// (the graph must keep owning <c>/init</c>-shaped behaviour); the merge is
/// stable and ordered by key for autocomplete.
/// </summary>
/// <param name="controlPlaneCatalog">Catalog over the control-plane command pack.</param>
public sealed class ChatSlashCatalog(IChatCommandCatalog controlPlaneCatalog)
{
    /// <summary>Lists built-ins plus control-plane commands, ordered by key.</summary>
    /// <param name="cancellationToken"></param>
    public async Task<IReadOnlyList<ChatSlashCommand>> ListAsync(CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, ChatSlashCommand>(StringComparer.Ordinal);

        foreach (var command in ChatSlashBuiltins.All)
        {
            merged[command.Key] = command;
        }

        foreach (var definition in await controlPlaneCatalog.ListCommandsAsync(cancellationToken))
        {
            // built-ins own their keys; a pack document may not override graph behaviour
            if (!merged.ContainsKey(definition.Key))
            {
                merged[definition.Key] = new ChatSlashCommand(
                    definition.Key,
                    definition.Name,
                    definition.Description,
                    definition.Body,
                    ChatSlashSources.ControlPlane);
            }
        }

        return [.. merged.Values.OrderBy(static command => command.Key, StringComparer.Ordinal)];
    }

    /// <summary>Finds one command by key (without the slash); null when unknown.</summary>
    /// <param name="key"></param>
    /// <param name="cancellationToken"></param>
    public async Task<ChatSlashCommand?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        return (await ListAsync(cancellationToken))
            .SingleOrDefault(command => string.Equals(command.Key, key, StringComparison.Ordinal));
    }
}
