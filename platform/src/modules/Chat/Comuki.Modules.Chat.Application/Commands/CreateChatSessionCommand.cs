namespace Comuki.Modules.Chat.Application.Commands;

/// <summary>Create-session command.</summary>
/// <param name="SubjectId">Acting subject (user or api key).</param>
/// <param name="ProjectId">Optional project scope.</param>
/// <param name="Title">Optional title.</param>
public sealed record CreateChatSessionCommand(Guid SubjectId, Guid? ProjectId, string? Title);
