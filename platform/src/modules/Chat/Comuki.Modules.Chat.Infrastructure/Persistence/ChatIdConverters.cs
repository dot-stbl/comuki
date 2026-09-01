using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Chat.Infrastructure.Persistence;

/// <summary>Value converters mapping Chat ids onto <c>uuid</c> columns.</summary>
public static class ChatIdConverters
{
    /// <summary><see cref="ChatSessionId"/> uuid converter.</summary>
    public static readonly ValueConverter<ChatSessionId, Guid> ChatSessionIdToUuid = new(
        static id => id.Value,
        static value => new ChatSessionId(value));

    /// <summary>Kernel <see cref="ProjectId"/> uuid converter (chat-local copy — modules do not share converters).</summary>
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdToUuid = new(
        static id => id.Value,
        static value => new ProjectId(value));
}
