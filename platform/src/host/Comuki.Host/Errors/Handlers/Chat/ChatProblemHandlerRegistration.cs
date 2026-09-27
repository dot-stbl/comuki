namespace Comuki.Host.Errors.Handlers.Chat;

/// <summary>
/// Registers the Chat surface's domain-error rows into the typed
/// problem-handler registry — the replacement for the deleted
/// retired <c>Chat</c> runner catch arms, in the same shape the surface
/// always shipped.
/// </summary>
public static class ChatProblemHandlerRegistration
{
    /// <summary>Registers the <c>ChatApprovePending</c> 409 and <c>BrainUnavailable</c> 503 rows.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddChatProblemHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IProblemHandler, ChatApprovePendingProblemHandler>();
        services.AddSingleton<IProblemHandler, BrainUnavailableProblemHandler>();

        return services;
    }
}
