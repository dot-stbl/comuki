using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.Memory;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// Subject-scope reachability across the gRPC enumerator boundaries.
/// <see cref="BrainAgent.RunAsync"/> opens its own <c>brain-agent</c>
/// scope on entry, but that scope is disposed on every yield — any tool
/// invoked <i>after</i> a <c>yield return</c> resumes outside the agent
/// method. The <see cref="BrainGrpcService"/> therefore opens its own
/// outer <c>brain-grpc</c> scope around the whole stream so the row-level
/// query filter (and any tool that consults
/// <see cref="ISubjectScopeAccessor.Current"/>) sees an established scope
/// at every enumerator boundary, not just while the agent method itself
/// is on the stack.
/// </summary>
public sealed class BrainGrpcServiceScopeShould
{
    [Fact(DisplayName = "Given the gRPC stream, when Think drains the enumerator, then a system scope is established on every tool call (covers enumerator boundaries)")]
    public async Task SystemScopeSurvivesEnumeratorBoundariesAsync()
    {
        var scopeAccessor = new AsyncLocalSubjectScopeAccessor();
        var store = new ScopeRecordingMemoryStore(scopeAccessor);
        var agent = Agent(store, scopeAccessor);
        var service = Service(agent, scopeAccessor);

        var chunks = await DrainAsync(service, new BrainRequest
        {
            Kind = BrainRequestKindKeys.Answer,
            Task = "tool-bearing answer",
        }, TestContext.Current.CancellationToken);

        chunks.ShouldNotBeEmpty();
        store.ScopeNamesPerSearch.ShouldNotBeEmpty();
        store.ScopeNamesPerSearch.ShouldAllBe(static name => name == "brain-agent" || name == "brain-grpc");
    }

    [Fact(DisplayName = "Given a tool that reads accessor.Current mid-stream, when Think drains, then no InvalidOperationException is thrown (regression for the unestablished-scope path)")]
    public async Task ToolThatReadsCurrentMidStream_DoesNotThrowAsync()
    {
        var scopeAccessor = new AsyncLocalSubjectScopeAccessor();
        // A memory store that consults `Current` (which throws on the
        // unestablished scope) instead of `CurrentOrNone`. Pre-fix code
        // threw InvalidOperationException once a tool call landed on a
        // resumed enumerator boundary; post-fix the outer `brain-grpc`
        // scope keeps an established scope available across boundaries.
        var store = new StrictCurrentMemoryStore(scopeAccessor);
        var agent = Agent(store, scopeAccessor);
        var service = Service(agent, scopeAccessor);

        // 1 tool call + 1 closing text ⇒ [iteration-chunk, final-chunk]
        var chunks = await DrainAsync(service, new BrainRequest
        {
            Kind = BrainRequestKindKeys.Answer,
            Task = "ask memory",
        }, TestContext.Current.CancellationToken);

        chunks.Count.ShouldBe(2);
        chunks.Last().IsFinal.ShouldBeTrue();
        store.CurrentSystemNames.ShouldNotBeEmpty();
    }

    private static BrainAgent Agent(IMemoryStore store, ISubjectScopeAccessor scopeAccessor)
    {
        // Scripted sequence: first response calls memory.search (drives a
        // tool invocation that lands on a SearchAsync call against the
        // supplied store), second response is a free-text answer that
        // closes the loop without another tool. The first MoveNext yields
        // the iteration chunk, the next MoveNext yields Final; the
        // enumerator resumes between those two yields, exactly the boundary
        // the outer `brain-grpc` scope is here to cover.
        var responses = new ChatResponse[]
        {
            ToolCall("call-1", "memory.search", new Dictionary<string, object?> { ["query"] = "deploys", ["limit"] = 5 }),
            Scripted.Text("answer"),
        };
        return new BrainAgent(
            TimeProvider.System,
            Substitute.For<IMemoryDigest>(),
            store,
            new FakeProfileCatalog([]),
            new StubActiveRunCatalog(),
            new StaticModelConfigProvider(),
            new StubExplorerReportReader(),
            scopeAccessor,
            Options.Create(new BrainOptions()),
            new ScriptedChatClientFactory(responses));
    }

    private static ChatResponse ToolCall(string callId, string name, IReadOnlyDictionary<string, object?> arguments)
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId, name, (IDictionary<string, object?>?)arguments)])]);
    }

    private static BrainGrpcService Service(BrainAgent agent, ISubjectScopeAccessor scopeAccessor)
    {
        return new BrainGrpcService(agent, scopeAccessor, NullLogger<BrainGrpcService>.Instance);
    }

    private static async Task<List<BrainChunk>> DrainAsync(
        BrainGrpcService service,
        BrainRequest request,
        CancellationToken cancellationToken)
    {
        var chunks = new List<BrainChunk>();
        await foreach (var chunk in service.Think(request, default).WithCancellation(cancellationToken))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}

/// <summary>
/// In-memory <see cref="IMemoryStore"/> that records every
/// <see cref="ISubjectScopeAccessor.CurrentOrNone"/> it sees on each
/// <c>SearchAsync</c>. The names it logs may be either the agent-level
/// <c>brain-agent</c> scope (visible while the agent method is on the
/// stack) or the gRPC-level <c>brain-grpc</c> scope (visible across
/// enumerator boundaries). A tool call that sees no scope at all would
/// produce a <c>null</c> entry here — that is the bug we are pinning
/// down.
/// </summary>
internal sealed class ScopeRecordingMemoryStore(ISubjectScopeAccessor scopeAccessor) : IMemoryStore
{
    public List<string?> ScopeNamesPerSearch { get; } = [];

    public List<MemoryFactWrite> Writes { get; } = [];

    public List<MemoryFactId> ForgottenIds { get; } = [];

    public MemoryFactQuery? LastQuery { get; private set; }

    public Task<MemoryFactView> WriteAsync(MemoryFactWrite write, CancellationToken cancellationToken = default)
    {
        Writes.Add(write);
        return Task.FromResult(new MemoryFactView(
            MemoryFactId.New(),
            write.Scope,
            MemoryFact.CanonicalKey(write.SubjectId),
            write.Kind,
            MemoryFact.CanonicalKey(write.TopicKey),
            write.Text,
            write.Source,
            write.CreatedBy,
            write.CreatedAt ?? DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<MemoryFactView>> SearchAsync(MemoryFactQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        ScopeNamesPerSearch.Add(scopeAccessor.CurrentOrNone?.SystemName);
        return Task.FromResult<IReadOnlyList<MemoryFactView>>([]);
    }

    public Task<IReadOnlyList<MemoryFactView>> ListAsync(
        MemoryScope scope,
        string subjectId,
        int limit = IMemoryStore.DefaultListLimit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ScopeNamesPerSearch.Add(scopeAccessor.CurrentOrNone?.SystemName);
        return Task.FromResult<IReadOnlyList<MemoryFactView>>([]);
    }

    public Task<bool> ForgetAsync(MemoryFactId id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<int> PromoteReadFactsAsync(DateTimeOffset now, int readThreshold, TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<int> DecayUnreadFactsAsync(DateTimeOffset now, TimeSpan unreadWindow, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<int> CountActiveFactsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }
}

/// <summary>
/// In-memory <see cref="IMemoryStore"/> that consults
/// <see cref="ISubjectScopeAccessor.Current"/> (not <c>CurrentOrNone</c>):
/// a tool author who writes <c>accessor.Current</c> does so on the
/// assumption that the ambient scope is always established. Pre-fix,
/// mid-stream tool calls landed on a yield boundary with no scope in
/// force and the call threw <see cref="InvalidOperationException"/>;
/// post-fix the outer <c>brain-grpc</c> scope is established on the
/// caller-side of the enumerator, so the call succeeds and the system
/// name is captured for assertion.
/// </summary>
internal sealed class StrictCurrentMemoryStore(ISubjectScopeAccessor scopeAccessor) : IMemoryStore
{
    public List<string> CurrentSystemNames { get; } = [];

    public MemoryFactQuery? LastQuery { get; private set; }

    public Task<MemoryFactView> WriteAsync(MemoryFactWrite write, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new MemoryFactView(
            MemoryFactId.New(),
            write.Scope,
            MemoryFact.CanonicalKey(write.SubjectId),
            write.Kind,
            MemoryFact.CanonicalKey(write.TopicKey),
            write.Text,
            write.Source,
            write.CreatedBy,
            write.CreatedAt ?? DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<MemoryFactView>> SearchAsync(MemoryFactQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        CurrentSystemNames.Add(scopeAccessor.Current.SystemName ?? "<none>");
        return Task.FromResult<IReadOnlyList<MemoryFactView>>([]);
    }

    public Task<IReadOnlyList<MemoryFactView>> ListAsync(
        MemoryScope scope,
        string subjectId,
        int limit = IMemoryStore.DefaultListLimit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        CurrentSystemNames.Add(scopeAccessor.Current.SystemName ?? "<none>");
        return Task.FromResult<IReadOnlyList<MemoryFactView>>([]);
    }

    public Task<bool> ForgetAsync(MemoryFactId id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<int> PromoteReadFactsAsync(DateTimeOffset now, int readThreshold, TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<int> DecayUnreadFactsAsync(DateTimeOffset now, TimeSpan unreadWindow, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public Task<int> CountActiveFactsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }
}
