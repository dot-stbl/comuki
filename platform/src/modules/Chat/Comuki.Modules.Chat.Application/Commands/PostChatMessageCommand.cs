namespace Comuki.Modules.Chat.Application.Commands;

/// <summary>Post-message command.</summary>
/// <param name="SessionId">Target session.</param>
/// <param name="Message">Raw user message.</param>
public sealed record PostChatMessageCommand(Guid SessionId, string Message);
