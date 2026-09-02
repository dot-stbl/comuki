using Comuki.Modules.Chat.Application.Graph.Factory;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Chat.Infrastructure.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;
using Voluta.Checkpoints.EntityFrameworkCore;
using Voluta.DependencyInjection;

namespace Comuki.Modules.Chat.Infrastructure;

/// <summary>Registration entry point for Chat persistence + the compiled graph.</summary>
public static class ChatPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="ChatDbContext"/> (Npgsql + snake_case + private
    /// migrations history via <see cref="ChatDbContext.ApplyOptions"/>), the
    /// singleton transcript store (context per call) and the Voluta EF Core
    /// checkpointer over the same context — the graph state table is
    /// <c>chat_checkpoints</c> inside the chat schema, so sessions resume
    /// from the same database days later. Finally compiles the chat graph
    /// with the application provider so nodes resolve their ports through DI.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    public static IServiceCollection AddChatPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<ChatDbContext>(options =>
            ChatDbContext.ApplyOptions(options, connectionString));

        services.AddVolutaCheckpoints(checkpoints => checkpoints.UseEntityFrameworkCore<ChatDbContext>());

        services.AddSingleton<IChatSessionStore, ChatSessionStore>();
        services.AddSingleton(static serviceProvider => ChatGraphFactory.Compile(
            serviceProvider,
            serviceProvider.GetRequiredService<Voluta.Abstractions.Checkpoint.ICheckpointer>()));

        return services;
    }
}
