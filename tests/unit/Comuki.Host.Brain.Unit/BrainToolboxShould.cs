using Comuki.Host.Brain.Brain.Exceptions;
using Comuki.Host.Brain.Brain.Tools;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// The brain tool surface: the seven tool names, emit_plan accept /
/// one-retry / hard-error semantics (including unknown profile keys),
/// memory.search defaults, formatting and semantic embedding, the
/// memory.write / memory.forget write path (scope posture, custom TTL,
/// validation) and the stub tools' honest empty outputs.
/// </summary>
public sealed class BrainToolboxShould
{
    private const string ValidPlan =
                             /*lang=json,strict*/
                             """{"summary":"s","nodes":[{"id":"n1","title":"t","profileKey":"implement","brief":"b"}],"edges":[]}""";

    [Fact(DisplayName = "Given the toolbox, when functions are built, then the seven contract tools are exposed")]
    public void BuildTheSevenContractTools()
    {
        var toolbox = Toolbox();

        var names = toolbox.BuildFunctions().Select(static function => function.Name).ToArray();

        names.ShouldBe([
            "memory.search",
            "memory.write",
            "memory.forget",
            "list_profiles",
            "list_active_runs",
            "read_explorer_report",
            "emit_plan",
        ]);
    }

    [Fact(DisplayName = "Given a built toolbox, when FindFunction is called, then known names resolve and unknown do not")]
    public void ResolveFunctionsByName()
    {
        var toolbox = Toolbox();
        toolbox.BuildFunctions();

        toolbox.FindFunction("memory.search").ShouldNotBeNull();
        toolbox.FindFunction("no-such-tool").ShouldBeNull();
    }

    [Fact(DisplayName = "Given a valid plan naming a catalog profile, when emit_plan is called, then it is accepted and consumed once")]
    public async Task AcceptValidPlanOnceAsync()
    {
        var toolbox = Toolbox();

        var accepted = await toolbox.EmitPlanAsync(ValidPlan);
        accepted.ShouldBe("plan accepted");

        toolbox.TryConsumeEmittedPlan(out var planJson).ShouldBeTrue();
        planJson.ShouldBe(ValidPlan);
        toolbox.TryConsumeEmittedPlan(out _).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an invalid plan, when emit_plan is called twice, then the first feeds errors back and the second throws")]
    public async Task RetryInvalidPlanExactlyOnceAsync()
    {
        var toolbox = Toolbox();
        const string cyclicPlan =
                             /*lang=json,strict*/
                             """{"summary":"s","nodes":[{"id":"n1","title":"t","profileKey":"implement","brief":"b"},{"id":"n2","title":"t","profileKey":"implement","brief":"b"}],"edges":[{"from":"n1","to":"n2"},{"from":"n2","to":"n1"}]}""";

        var first = await toolbox.EmitPlanAsync(cyclicPlan);
        first.ShouldStartWith("plan rejected");
        first.ShouldContain("acyclic");

        await Should.ThrowAsync<BrainInvalidPlanException>(() => toolbox.EmitPlanAsync(cyclicPlan));
    }

    [Fact(DisplayName = "Given a plan with empty node fields, when emit_plan rejects it, then the retry feedback names every empty field")]
    public async Task FeedEmptyFieldErrorsBackForTheRetryAsync()
    {
        var toolbox = Toolbox();
        const string hollowPlan =
                             /*lang=json,strict*/
                             """{"summary":"s","nodes":[{"id":"","title":"","profileKey":"","brief":""}],"edges":[]}""";

        var feedback = await toolbox.EmitPlanAsync(hollowPlan);

        feedback.ShouldStartWith("plan rejected");
        feedback.ShouldContain("node id must not be empty");
        feedback.ShouldContain("title must not be empty");
        feedback.ShouldContain("profile key must not be empty");
        feedback.ShouldContain("brief must not be empty");
        feedback.ShouldContain("call emit_plan again");
    }

    [Fact(DisplayName = "Given the built tool surface, when the emit_plan description is read, then it pins the full plan schema and rules")]
    public void PinThePlanSchemaInTheEmitPlanDescription()
    {
        var toolbox = Toolbox();
        toolbox.BuildFunctions();

        var description = toolbox.FindFunction("emit_plan")!.Description;

        description.ShouldContain("\"summary\"");
        description.ShouldContain("\"id\"");
        description.ShouldContain("\"title\"");
        description.ShouldContain("\"profileKey\"");
        description.ShouldContain("\"brief\"");
        description.ShouldContain("\"edges\"");
        description.ShouldContain("non-empty");
        description.ShouldContain("unique");
        description.ShouldContain("list_profiles");
    }

    [Fact(DisplayName = "Given a plan naming an unknown profile, when emit_plan is called, then it is rejected with the catalog hint")]
    public async Task RejectUnknownProfileKeysAsync()
    {
        var toolbox = Toolbox();
        const string unknownProfilePlan =
                             /*lang=json,strict*/
                             """{"summary":"s","nodes":[{"id":"n1","title":"t","profileKey":"ghost-profile","brief":"b"}],"edges":[]}""";

        var rejected = await toolbox.EmitPlanAsync(unknownProfilePlan);

        rejected.ShouldStartWith("plan rejected");
        rejected.ShouldContain("'ghost-profile' is not in the profile catalog");
    }

    [Fact(DisplayName = "Given stored facts, when memory.search runs, then facts render as kind/topic/text lines")]
    public async Task FormatMemorySearchResultsAsync()
    {
        var store = new FakeMemoryStore([Fact("deploy", "deploys use docker compose")]);
        var toolbox = Toolbox(store);

        var output = await toolbox.SearchMemoryAsync("docker");

        output.ShouldBe("[standing] deploy: deploys use docker compose");
    }

    [Fact(DisplayName = "Given no facts, when memory.search runs, then the honest empty answer comes back")]
    public async Task ReportEmptyMemorySearchAsync()
    {
        var toolbox = Toolbox();

        var output = await toolbox.SearchMemoryAsync("anything");

        output.ShouldBe("no memory facts for 'anything'");
    }

    [Fact(DisplayName = "Given a configured embedding model, when memory.search runs, then the query vector rides the store query (semantic path)")]
    public async Task EmbedTheSearchQueryAsync()
    {
        var store = new FakeMemoryStore([Fact("deploy", "deploys use docker compose")]);
        var embedder = new FakeEmbeddingClient("fake");
        var toolbox = Toolbox(store, embedder: embedder);

        await toolbox.SearchMemoryAsync("container deploys");

        embedder.EmbeddedTexts.ShouldContain("container deploys");
        store.LastQuery?.Embedding.ShouldBe(FakeEmbeddingClient.Vector);
    }

    [Fact(DisplayName = "Given the noop provider (no model configured), when memory.search runs, then no vector is computed and the fallback ranking answers")]
    public async Task SkipEmbeddingForTheNoopProviderAsync()
    {
        var store = new FakeMemoryStore([]);
        var embedder = new FakeEmbeddingClient("noop");
        var toolbox = Toolbox(store, embedder: embedder);

        await toolbox.SearchMemoryAsync("anything");

        embedder.EmbeddedTexts.ShouldBeEmpty();
        store.LastQuery?.Embedding.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a failing embedding provider, when memory.search runs, then search still answers via the fallback")]
    public async Task FallBackWhenEmbeddingFailsAsync()
    {
        var store = new FakeMemoryStore([Fact("deploy", "deploys use docker compose")]);
        var toolbox = Toolbox(store, embedder: new ThrowingEmbeddingClient());

        var output = await toolbox.SearchMemoryAsync("docker");

        output.ShouldBe("[standing] deploy: deploys use docker compose");
        store.LastQuery?.Embedding.ShouldBeNull();
    }

    [Fact(DisplayName = "Given the memory tool signatures, when inspected, then none exposes a scope, subject or project parameter the model could ever fill in (leak 2 — the unsafe call is unrepresentable)")]
    public void ExposeNoScopeOrSubjectParameter()
    {
        // memory.search used to take a free `scope`/`subject` pair the
        // model itself supplied and trusted verbatim — a model call (or
        // prompt-injected content that talks it into one) naming another
        // subject's user scope, or another project's project scope, got
        // that subject's/project's private facts back as text. Rather
        // than gate that argument at runtime, the fix removed it: this
        // asserts the removal (write and forget included) can't quietly
        // regress back in.
        var unsafeParameters = new[] { "scope", "subject", "subjectId", "projectId" };
        var methods = new[]
        {
            nameof(BrainToolbox.SearchMemoryAsync),
            nameof(BrainToolbox.WriteMemoryAsync),
            nameof(BrainToolbox.ForgetMemoryAsync),
        };

        foreach (var method in methods)
        {
            var parameters = typeof(BrainToolbox)
                .GetMethod(method)!
                .GetParameters()
                .Select(static parameter => parameter.Name)
                .ToArray();

            foreach (var unsafeParameter in unsafeParameters)
            {
                parameters.ShouldNotContain(unsafeParameter, $"{method} must not expose '{unsafeParameter}'");
            }
        }
    }

    [Fact(DisplayName = "Given another subject's user-scoped fact and another project's project-scoped fact, when memory.search runs, then neither comes back — only the global corpus is reachable (leak 2)")]
    public async Task ReachOnlyTheGlobalCorpusAsync()
    {
        var store = new FakeMemoryStore(
        [
            Victim("salary", "negotiated a higher rate"),
            new MemoryFactView(
                MemoryFactId.New(),
                MemoryScope.Project,
                "other-project",
                MemoryFactKind.Standing,
                "roadmap",
                "ships the v3 migration in secret",
                MemorySource.Chat,
                "tester",
                DateTimeOffset.UtcNow),
        ]);
        var toolbox = Toolbox(store);

        var output = await toolbox.SearchMemoryAsync("anything");

        output.ShouldNotContain("negotiated a higher rate");
        output.ShouldNotContain("ships the v3 migration in secret");
        output.ShouldBe("no memory facts for 'anything'");
    }

    [Fact(DisplayName = "Given a memory.write call, when it runs, then the fact lands in the global corpus signed by the brain")]
    public async Task WriteTheFactIntoTheGlobalCorpusAsync()
    {
        var store = new FakeMemoryStore([]);
        var toolbox = Toolbox(store);

        var output = await toolbox.WriteMemoryAsync("Deploy Prefs", "deploys use docker compose", "standing");

        output.ShouldBe("remembered 'deploy prefs' (standing)");
        var write = store.Writes.ShouldHaveSingleItem();
        write.Scope.ShouldBe(MemoryScope.Global);
        write.SubjectId.ShouldBe(MemoryScopeKeys.GlobalSubject);
        write.Kind.ShouldBe(MemoryFactKind.Standing);
        write.TopicKey.ShouldBe("Deploy Prefs");
        write.Text.ShouldBe("deploys use docker compose");
        write.Source.ShouldBe(MemorySource.Chat);
        write.CreatedBy.ShouldBe(BrainToolbox.WriteActor);
        write.CreatedAt.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an ephemeral write with ttlHours, when it runs, then created_at is backdated so the fixed sweep horizon expires it on schedule")]
    public async Task BackdateCustomTtlWritesAsync()
    {
        var store = new FakeMemoryStore([]);
        var toolbox = Toolbox(store);

        await toolbox.WriteMemoryAsync("task-note", "scratch note", "ephemeral", ttlHours: 2);

        var write = store.Writes.ShouldHaveSingleItem();
        write.Kind.ShouldBe(MemoryFactKind.Ephemeral);
        write.CreatedAt.ShouldBe(FixedTime.Now - (MemoryFactPolicy.EphemeralTtl - TimeSpan.FromHours(2)));
    }

    [Theory(DisplayName = "Given an invalid memory.write kind, when it runs, then a corrective message comes back instead of a write")]
    [InlineData("permanent")]
    [InlineData("")]
    public async Task RejectInvalidWriteKindsAsync(string kind)
    {
        var store = new FakeMemoryStore([]);
        var toolbox = Toolbox(store);

        var output = await toolbox.WriteMemoryAsync("topic", "text", kind);

        output.ShouldStartWith("memory.write rejected");
        store.Writes.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a standing write with ttlHours, when it runs, then it is rejected — ttl applies to ephemeral facts only")]
    public async Task RejectTtlOnStandingWritesAsync()
    {
        var store = new FakeMemoryStore([]);
        var toolbox = Toolbox(store);

        var output = await toolbox.WriteMemoryAsync("topic", "text", "standing", ttlHours: 5);

        output.ShouldStartWith("memory.write rejected");
        store.Writes.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a non-positive ttlHours, when memory.write runs, then it is rejected")]
    public async Task RejectNonPositiveTtlAsync()
    {
        var store = new FakeMemoryStore([]);
        var toolbox = Toolbox(store);

        var output = await toolbox.WriteMemoryAsync("topic", "text", "ephemeral", ttlHours: 0);

        output.ShouldStartWith("memory.write rejected");
        store.Writes.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a configured embedding model, when memory.write runs, then the fact text is embedded with the write")]
    public async Task EmbedWritesAsync()
    {
        var store = new FakeMemoryStore([]);
        var embedder = new FakeEmbeddingClient("fake");
        var toolbox = Toolbox(store, embedder: embedder);

        await toolbox.WriteMemoryAsync("deploy", "deploys use docker compose", "standing");

        embedder.EmbeddedTexts.ShouldBe(["deploys use docker compose"]);
        store.Writes.ShouldHaveSingleItem().Embedding.ShouldBe(FakeEmbeddingClient.Vector);
    }

    [Fact(DisplayName = "Given a failing embedding provider, when memory.write runs, then the fact still lands without a vector")]
    public async Task WriteWithoutEmbeddingWhenTheProviderFailsAsync()
    {
        var store = new FakeMemoryStore([]);
        var toolbox = Toolbox(store, embedder: new ThrowingEmbeddingClient());

        var output = await toolbox.WriteMemoryAsync("deploy", "deploys use docker compose", "standing");

        output.ShouldStartWith("remembered");
        store.Writes.ShouldHaveSingleItem().Embedding.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a stored global fact, when memory.forget runs on its topic, then the fact is deleted and confirmed")]
    public async Task ForgetTheActiveFactByTopicAsync()
    {
        var fact = Fact("deploy", "deploys use docker compose");
        var store = new FakeMemoryStore([fact]);
        var toolbox = Toolbox(store);

        var output = await toolbox.ForgetMemoryAsync("Deploy");

        output.ShouldBe("forgot 'deploy'");
        store.ForgottenIds.ShouldBe([fact.Id]);
    }

    [Fact(DisplayName = "Given no fact under the topic, when memory.forget runs, then the honest absence comes back")]
    public async Task ReportForgetAbsenceAsync()
    {
        var toolbox = Toolbox();

        var output = await toolbox.ForgetMemoryAsync("nothing-here");

        output.ShouldBe("no memory fact under 'nothing-here'");
    }

    [Fact(DisplayName = "Given another subject's fact, when memory.forget runs, then it is never a candidate — the topic scan reads the global corpus only (leak 2)")]
    public async Task ForgetScansOnlyTheGlobalCorpusAsync()
    {
        var victim = Victim("salary", "negotiated a higher rate");
        var store = new FakeMemoryStore([victim]);
        var toolbox = Toolbox(store);

        var output = await toolbox.ForgetMemoryAsync("salary");

        output.ShouldBe("no memory fact under 'salary'");
        store.ForgottenIds.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given catalog profiles, when list_profiles runs, then each renders key and name")]
    public async Task ListCatalogProfilesAsync()
    {
        var toolbox = Toolbox();

        var output = await toolbox.ListProfilesAsync();

        output.ShouldContain("implement — Implementer: writes the code");
    }

    [Fact(DisplayName = "Given an empty catalog, when list_profiles runs, then the honest empty answer comes back")]
    public async Task ReportEmptyCatalogAsync()
    {
        var toolbox = Toolbox(profiles: []);

        var output = await toolbox.ListProfilesAsync();

        output.ShouldBe("profile catalog is empty");
    }

    [Fact(DisplayName = "Given the slice-A stubs, when runs and reports are read, then they answer absence honestly")]
    public async Task ReportStubAbsenceAsync()
    {
        var toolbox = Toolbox();

        (await toolbox.ListActiveRunsAsync()).ShouldBe("no active runs");
        (await toolbox.ReadExplorerReportAsync()).ShouldBe("no explorer report available");
    }

    private static BrainToolbox Toolbox(
        FakeMemoryStore? store = null,
        IReadOnlyList<ProfileDefinition>? profiles = null,
        IEmbeddingClient? embedder = null)
    {
        return new BrainToolbox(
            store ?? new FakeMemoryStore([]),
            FixedTime.Provider,
            new FakeProfileCatalog(profiles ?? [Profile("implement", "Implementer", "writes the code")]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            embedder);
    }

    private static ProfileDefinition Profile(string key, string name, string description)
    {
        return new ProfileDefinition(key, name, description, [], null);
    }

    private static MemoryFactView Fact(string topicKey, string text)
    {
        return new MemoryFactView(
            MemoryFactId.New(),
            MemoryScope.Global,
            "global",
            MemoryFactKind.Standing,
            topicKey,
            text,
            MemorySource.Chat,
            "tester",
            DateTimeOffset.UtcNow);
    }

    private static MemoryFactView Victim(string topicKey, string text)
    {
        return new MemoryFactView(
            MemoryFactId.New(),
            MemoryScope.User,
            "victim-user",
            MemoryFactKind.Standing,
            topicKey,
            text,
            MemorySource.Chat,
            "victim-user",
            DateTimeOffset.UtcNow);
    }
}

/// <summary>Deterministic clock: fixed "now", the same value the backdating tests align against.</summary>
file static class FixedTime
{
    public static readonly TimeProvider Provider = new FixedTimeProvider();

    public static DateTimeOffset Now => Provider.GetUtcNow();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}

/// <summary>In-memory profile catalog: fixed list, no IO.</summary>
internal sealed class FakeProfileCatalog(IReadOnlyList<ProfileDefinition> profiles) : IProfileCatalog
{
    public Task<IReadOnlyList<ProfileDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(profiles);
    }

    public Task<ProfileDefinition?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(profiles.FirstOrDefault(profile => profile.Key == key));
    }
}

/// <summary>
/// In-memory memory store for tool tests: search answers fixed facts and
/// records the last query; write/forget record what the tools did.
/// </summary>
// TODO(canon #12): extract this fake to a shared *.Testing project — separate task with its own csproj/slnx changes.
internal sealed class FakeMemoryStore(IReadOnlyList<MemoryFactView> facts) : IMemoryStore
{
    private readonly List<MemoryFactView> written = [];

    public List<MemoryFactWrite> Writes { get; } = [];

    public List<MemoryFactId> ForgottenIds { get; } = [];

    public MemoryFactQuery? LastQuery { get; private set; }

    public Task<MemoryFactView> WriteAsync(MemoryFactWrite write, CancellationToken cancellationToken = default)
    {
        Writes.Add(write);

        // mirror the store's shape the tools rely on: canonicalized topic
        // key, fresh view — createdAt override included
        var view = new MemoryFactView(
            MemoryFactId.New(),
            write.Scope,
            MemoryFact.CanonicalKey(write.SubjectId),
            write.Kind,
            MemoryFact.CanonicalKey(write.TopicKey),
            write.Text,
            write.Source,
            write.CreatedBy,
            write.CreatedAt ?? DateTimeOffset.UtcNow);
        written.Add(view);
        return Task.FromResult(view);
    }

    public Task<IReadOnlyList<MemoryFactView>> SearchAsync(MemoryFactQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(Visible(query.Scope, query.SubjectId, query.Limit));
    }

    public Task<IReadOnlyList<MemoryFactView>> ListAsync(
        MemoryScope scope,
        string subjectId,
        int limit = IMemoryStore.DefaultListLimit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        LastQuery = new MemoryFactQuery(Scope: scope, SubjectId: subjectId, Limit: limit);
        return Task.FromResult(Visible(scope, subjectId, limit));
    }

    public Task<bool> ForgetAsync(MemoryFactId id, CancellationToken cancellationToken = default)
    {
        ForgottenIds.Add(id);
        return Task.FromResult(facts.Any(fact => fact.Id == id) || written.Any(fact => fact.Id == id));
    }

    public Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("sweep is out of scope for the toolbox fake");
    }

    public Task<int> PromoteReadFactsAsync(DateTimeOffset now, int readThreshold, TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("promotion is out of scope for the toolbox fake");
    }

    public Task<int> DecayUnreadFactsAsync(DateTimeOffset now, TimeSpan unreadWindow, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("decay is out of scope for the toolbox fake");
    }

    public Task<int> CountActiveFactsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("counting is out of scope for the toolbox fake");
    }

    private IReadOnlyList<MemoryFactView> Visible(MemoryScope? scope, string? subjectId, int limit)
    {
        return [.. facts
            .Concat(written)
            .Where(fact => scope is null || fact.Scope == scope)
            .Where(fact => subjectId is null || fact.SubjectId == MemoryFact.CanonicalKey(subjectId))
            .Take(limit)];
    }
}

/// <summary>Scriptable embedding client: one fixed vector, recorded inputs.</summary>
internal sealed class FakeEmbeddingClient(string providerName) : IEmbeddingClient
{
    /// <summary>The single vector every embed call answers with.</summary>
    public static readonly float[] Vector = [0.25f, 0.75f];

    public List<string> EmbeddedTexts { get; } = [];

    public string ProviderName { get; } = providerName;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        EmbeddedTexts.Add(text);
        return Task.FromResult(Vector);
    }

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        EmbeddedTexts.AddRange(texts);
        return Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(static _ => Vector)]);
    }
}

/// <summary>Embedding client that always fails — the degradation-path fixture.</summary>
internal sealed class ThrowingEmbeddingClient : IEmbeddingClient
{
    public string ProviderName => "broken";

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new HttpRequestException("embedding provider unreachable");
    }

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        throw new HttpRequestException("embedding provider unreachable");
    }
}
