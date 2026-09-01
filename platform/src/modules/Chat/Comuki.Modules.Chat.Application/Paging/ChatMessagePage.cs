using Comuki.Modules.Chat.Domain.Messages;

namespace Comuki.Modules.Chat.Application.Paging;

/// <summary>
/// One page of the chat transcript, oldest first. Pages are 1-based;
/// <see cref="Total"/> counts every message of the session.
/// </summary>
/// <param name="Items">Messages of this page, oldest first.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Page size the caller asked for.</param>
/// <param name="Total">Total message count of the session.</param>
public sealed record ChatMessagePage(
    IReadOnlyList<ChatMessage> Items,
    int Page,
    int PageSize,
    int Total);
