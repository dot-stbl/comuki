using Comuki.Modules.Chat.Application;
using Comuki.Modules.Chat.Application.Graph.Factory;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Application.Sessions;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.Memory;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Voluta.Checkpoint;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Boots the chat application layer over fake ports and the Voluta
/// in-memory checkpointer — the graph, turn service and journalist are the
/// real production types; only the boundaries (brain, digest, store, tools,
/// command catalog) are fakes.
/// </summary>
public sealed class ChatHarness : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    private ChatHarness(
        ServiceProvider provider,
        FakeBrainClient brain,
        FakeMemoryDigest digest,
        FakeChatSessionStore store,
        FakeChatToolExecutor tools)
    {
        this.provider = provider;
        Brain = brain;
        Digest = digest;
        Store = store;
        Tools = tools;
        Turns = provider.GetRequiredService<IChatTurnService>();
        Sessions = provider.GetRequiredService<ChatSessionService>();
    }

    /// <summary>Recorded brain invocations.</summary>
    public FakeBrainClient Brain { get; }

    /// <summary>Recorded digest requests.</summary>
    public FakeMemoryDigest Digest { get; }

    /// <summary>In-memory transcript.</summary>
    public FakeChatSessionStore Store { get; }

    /// <summary>Recorded tool calls.</summary>
    public FakeChatToolExecutor Tools { get; }

    /// <summary>Real turn driver.</summary>
    public IChatTurnService Turns { get; }

    /// <summary>Real session lifecycle service.</summary>
    public ChatSessionService Sessions { get; }

    /// <summary>Builds the harness with a non-empty digest so digest journaling is observable.</summary>
    /// <param name="commands">Extra control-plane commands exposed to the slash catalog.</param>
    /// <returns>Disposable harness.</returns>
    public static ChatHarness Create(IReadOnlyList<ChatCommandDefinition>? commands = null)
    {
        var brain = new FakeBrainClient();
        var digest = new FakeMemoryDigest(FakeMemoryDigest.DefaultDigest);
        var store = new FakeChatSessionStore();
        var tools = new FakeChatToolExecutor();
        var checkpointer = new InMemoryCheckpointer();

        var services = new ServiceCollection();
        services.AddChatApplication();
        services.AddSingleton<IBrainClient>(brain);
        services.AddSingleton<IMemoryDigest>(digest);
        services.AddSingleton<IChatCommandCatalog>(new FakeChatCommandCatalog(commands ?? []));
        services.AddSingleton<IChatSessionStore>(store);
        services.AddSingleton<IChatToolExecutor>(tools);
        services.AddSingleton(serviceProvider => ChatGraphFactory.Compile(serviceProvider, checkpointer));

        var provider = services.BuildServiceProvider();
        return new ChatHarness(provider, brain, digest, store, tools);
    }

    /// <summary>Creates an active session owned by an arbitrary subject.</summary>
    /// <param name="projectId">Optional project scope (guid form).</param>
    /// <returns>Created session id.</returns>
    public async Task<ChatSessionId> NewSessionAsync(string? projectId = null)
    {
        var subjectId = Guid.NewGuid();
        var session = await Sessions.CreateAsync(
            subjectId,
            projectId is null ? null : new ProjectId(Guid.Parse(projectId)),
            title: null);
        SubjectId = subjectId;
        lastSession = session;
        return session.Id;
    }

    /// <summary>Loads the session aggregate the harness created.</summary>
    /// <param name="sessionId">Session to load.</param>
    /// <returns>Session aggregate (refreshed from the store, falling back to the created one).</returns>
    public async Task<Domain.Sessions.ChatSession> SessionAsync(ChatSessionId sessionId)
    {
        return await Sessions.FindOwnedAsync(sessionId, SubjectId) ?? lastSession!;
    }

    /// <summary>Owner subject of the last <see cref="NewSessionAsync"/> session.</summary>
    public Guid SubjectId { get; private set; }

    private Domain.Sessions.ChatSession? lastSession;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return provider.DisposeAsync();
    }
}

/// <summary>Fixed command pack for the slash catalog tests.</summary>
public sealed class FakeChatCommandCatalog(IReadOnlyList<ChatCommandDefinition> commands) : IChatCommandCatalog
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ChatCommandDefinition>> ListCommandsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(commands);
    }
}
