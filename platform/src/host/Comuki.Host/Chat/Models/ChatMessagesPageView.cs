using Comuki.Modules.Chat.Application.Paging;

namespace Comuki.Host.Chat.Models;

/// <summary>Transcript page read model.</summary>
public sealed class ChatMessagesPageView
{
    /// <summary>Rows of this page, oldest first.</summary>
    public required IReadOnlyList<ChatMessageView> Items { get; init; }

    /// <summary>1-based page number.</summary>
    public required int Page { get; init; }

    /// <summary>Page size.</summary>
    public required int PageSize { get; init; }

    /// <summary>Total message count of the session.</summary>
    public required int Total { get; init; }

    /// <summary>Maps the application page.</summary>
    /// <param name="page"></param>
    public static ChatMessagesPageView Of(ChatMessagePage page)
    {
        return new ChatMessagesPageView
        {
            Items = [.. page.Items.Select(ChatMessageView.Of)],
            Page = page.Page,
            PageSize = page.PageSize,
            Total = page.Total,
        };
    }
}
