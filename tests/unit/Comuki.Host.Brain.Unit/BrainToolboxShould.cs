using Comuki.Host.Brain.Brain.Exceptions;
using Comuki.Host.Brain.Brain.Tools;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// The brain tool surface: the five S5 tool names, emit_plan accept /
/// one-retry / hard-error semantics (including unknown profile keys),
/// memory.search defaults and formatting, and the stub tools' honest
/// empty outputs.
/// </summary>
public sealed class BrainToolboxShould
{
    private const string ValidPlan =
                             /*lang=json,strict*/
                             """{"summary":"s","nodes":[{"id":"n1","title":"t","profileKey":"implement","brief":"b"}],"edges":[]}""";

    [Fact(DisplayName = "Given the toolbox, when functions are built, then the five contract tools are exposed")]
    public void BuildTheFiveContractTools()
    {
        var toolbox = Toolbox();

        var names = toolbox.BuildFunctions().Select(static function => function.Name).ToArray();

        names.ShouldBe(["memory.search", "list_profiles", "list_active_runs", "read_explorer_report", "emit_plan"]);
    }

    [Fact(DisplayName = "Given a built toolbox, when FindFunction is called, then known names resolve and unknown do not")]
    public void ResolveFunctionsByName()
    {
        var toolbox = Toolbox();
        _ = toolbox.BuildFunctions();

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

    [Fact(DisplayName = "Given an unknown scope, when memory.search runs, then ArgumentException names the expectation")]
    public void RefuseUnknownMemoryScope()
    {
        var toolbox = Toolbox();

        Should.Throw<ArgumentException>(() => toolbox.SearchMemoryAsync("q", scope: "galaxy"));
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
        IReadOnlyList<ProfileDefinition>? profiles = null)
    {
        return new BrainToolbox(
            store ?? new FakeMemoryStore([]),
            new FakeProfileCatalog(profiles ?? [Profile("implement", "Implementer", "writes the code")]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader());
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

/// <summary>In-memory memory store for tool tests: search answers fixed facts.</summary>
internal sealed class FakeMemoryStore(IReadOnlyList<MemoryFactView> facts) : IMemoryStore
{
    public Task<MemoryFactView> WriteAsync(MemoryFactWrite write, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("write is out of scope for the toolbox fake");
    }

    public Task<IReadOnlyList<MemoryFactView>> SearchAsync(MemoryFactQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MemoryFactView> results = [.. facts
            .Where(fact => query.Scope is null || fact.Scope == query.Scope)
            .Where(fact => query.SubjectId is null || fact.SubjectId == query.SubjectId)
            .Where(fact => query.Kind is null || fact.Kind == query.Kind)
            .Take(query.Limit)];

        return Task.FromResult(results);
    }

    public Task<IReadOnlyList<MemoryFactView>> ListAsync(MemoryScope scope, string subjectId, CancellationToken cancellationToken = default)
    {
        return SearchAsync(new MemoryFactQuery(Scope: scope, SubjectId: subjectId), cancellationToken);
    }

    public Task<bool> ForgetAsync(MemoryFactId id, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("forget is out of scope for the toolbox fake");
    }

    public Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("sweep is out of scope for the toolbox fake");
    }
}
