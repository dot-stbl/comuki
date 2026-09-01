using Comuki.Modules.Chat.Application.Slash;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Results;

namespace Comuki.Modules.Chat.Application.Graph;

/// <summary>
/// Expands a slash message into the router's channel writes. Unknown
/// commands fall through to the brain as a plain chat message (the brain
/// explains the command does not exist); <c>/init</c> (re)starts the wizard
/// node; every other command's body plus the user arguments becomes the
/// brain task.
/// </summary>
/// <param name="slashCatalog">Merged built-in + control-plane command catalog.</param>
public sealed class ChatSlashExpander(ChatSlashCatalog slashCatalog)
{
    /// <summary>Expands one slash message into routing writes.</summary>
    /// <param name="message">Raw user message starting with <c>/</c>.</param>
    /// <param name="cancellationToken"></param>
    public async Task<NodeResult> ExpandAsync(string message, CancellationToken cancellationToken = default)
    {
        var separator = message.IndexOf(' ');
        var key = (separator < 0 ? message : message[..separator])[1..];
        var arguments = separator < 0 ? string.Empty : message[(separator + 1)..].Trim();
        var command = await slashCatalog.FindAsync(key, cancellationToken);

        if (command is null)
        {
            return NodeResult.Continue(
                new ChannelWrite(ChatChannels.Phase, ChatPhases.Think),
                new ChannelWrite(ChatChannels.Task, message),
                new ChannelWrite(ChatChannels.BrainKind, "chat"));
        }

        if (key == ChatSlashBuiltins.InitKey)
        {
            // /init (re)starts the wizard from step one with fresh answers
            return NodeResult.Continue(
                new ChannelWrite(ChatChannels.Phase, ChatPhases.Init),
                new ChannelWrite(ChatChannels.Wizard, ChatSlashBuiltins.InitKey),
                new ChannelWrite(ChatChannels.InitStep, "0"),
                new ChannelWrite(ChatChannels.InitAnswersJson, "{}"));
        }

        return NodeResult.Continue(
            new ChannelWrite(ChatChannels.Phase, ChatPhases.Think),
            new ChannelWrite(ChatChannels.Task, ChatSlashTaskBody.Of(command.Body, arguments)),
            new ChannelWrite(ChatChannels.BrainKind, "chat"));
    }
}

/// <summary>Command body + user arguments → brain task text.</summary>
file static class ChatSlashTaskBody
{
    public static string Of(string body, string arguments)
    {
        return body.Length == 0 || arguments.Length == 0
            ? (body.Length == 0 ? arguments : body)
            : body + "\n\n---\nUser arguments: " + arguments;
    }
}
