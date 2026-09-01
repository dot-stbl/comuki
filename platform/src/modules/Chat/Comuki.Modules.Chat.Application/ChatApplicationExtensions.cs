using Comuki.Modules.Chat.Application.Commands;
using Comuki.Modules.Chat.Application.Graph;
using Comuki.Modules.Chat.Application.Sessions;
using Comuki.Modules.Chat.Application.Slash;
using Comuki.Shared.Contracts.Plans;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Chat.Application;

/// <summary>Registration entry point for the Chat module application layer.</summary>
public static class ChatApplicationExtensions
{
    /// <summary>
    /// Registers the turn services, slash catalog, graph nodes and
    /// validators. Everything is singleton — the nodes are stateless and
    /// resolve their ports per invocation through the graph's service
    /// provider. The host composition additionally provides
    /// <see cref="IBrainClient"/>, <see cref="IMemoryDigest"/>-ports and the
    /// compiled graph itself.
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddChatApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<ChatSlashCatalog>();
        services.AddSingleton<ChatSlashExpander>();
        services.AddSingleton<RouteNode>();
        services.AddSingleton<ClarifyNode>();
        services.AddSingleton<InitNode>();
        services.AddSingleton<ConfirmNode>();
        services.AddSingleton<ActNode>();
        services.AddSingleton<ThinkNode>();

        services.AddSingleton<IValidator<Plan>, PlanValidator>();
        services.AddSingleton<IValidator<CreateChatSessionCommand>, CreateChatSessionValidator>();
        services.AddSingleton<IValidator<PostChatMessageCommand>, PostChatMessageValidator>();

        services.AddSingleton<ChatSessionService>();
        services.AddSingleton<ChatTurnJournalist>();
        services.AddSingleton<IChatTurnService, ChatTurnService>();

        return services;
    }
}
