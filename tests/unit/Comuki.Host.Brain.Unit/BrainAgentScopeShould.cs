using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.Memory;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// The scope hook on <see cref="BrainRequest"/>: the brain fetches a
/// scope-aware digest via <see cref="IMemoryDigest"/> only when the
/// caller sets <c>ScopeKind</c> + a valid <c>SubjectId</c>; otherwise the
/// legacy global-only behaviour is preserved verbatim. The model-side
/// tool surface (BrainToolbox) deliberately stays global-only — see the
/// security note on its <c>SearchMemoryAsync</c> method — so the only
/// scope-aware path through the brain lives in
/// <c>BrainAgentDigestBuilder</c> (a <c>file static class</c> in
/// <c>BrainAgent.cs</c> co-located with <c>BrainToolExecution</c>).
/// The tests below pin that path on the <see cref="IMemoryDigest"/>
/// mock so a future refactor cannot quietly re-route scope-aware fetches
/// through the model-controlled <c>memory.search</c> tool.
/// </summary>
public sealed class BrainAgentScopeShould
{
    private const string ProjectDigestText =
        "[Standing] discovery.finding.install: pnpm install";

    [Fact(DisplayName = "Given a request with no ScopeKind, when the loop runs, then IMemoryDigest is never called and the user message carries only the caller-built ContextJson")]
    public async Task LegacyRequest_DoesNotCallMemoryDigestAsync()
    {
        var digest = Substitute.For<IMemoryDigest>();
        var scripted = new ScriptedChatClient([Scripted.Text("legacy answer")]);
        var agent = Agent(digest, scripted);

        var request = new BrainRequest
        {
            Kind = BrainRequestKindKeys.Answer,
            Task = "describe the project",
            ContextJson = /*lang=json,strict*/ """{"runs": []}""",
        };

        var chunks = await StreamAsync(agent, request, TestContext.Current.CancellationToken);

        chunks.ShouldHaveSingleItem().FinalJson.ShouldBe("legacy answer");
        await digest.DidNotReceiveWithAnyArgs().BuildDigestAsync(default!, TestContext.Current.CancellationToken);

        var userText = scripted.Calls[0][1].Text;
        userText.ShouldNotBeNull();
        userText!.ShouldContain("# Context");
        userText.ShouldContain(/*lang=json,strict*/ "{\"runs\": []}");
        userText.ShouldNotContain("# Scoped memory");
    }

    [Fact(DisplayName = "Given a request with ScopeKind = project and a valid SubjectId, when the loop runs, then IMemoryDigest is called with the matching scope and the digest is prepended to ContextJson")]
    public async Task ProjectScope_CallsMemoryDigestAndPrependsAsync()
    {
        var subjectId = Guid.NewGuid();
        var digest = Substitute.For<IMemoryDigest>();
        digest.BuildDigestAsync(Arg.Any<MemoryDigestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ProjectDigestText);
        var scripted = new ScriptedChatClient([Scripted.Text("project-aware answer")]);
        var agent = Agent(digest, scripted);

        var request = new BrainRequest
        {
            Kind = BrainRequestKindKeys.Answer,
            Task = "how do we install?",
            ContextJson = /*lang=json,strict*/ """{"runs": []}""",
            ScopeKind = MemoryDigestScopes.Project,
            SubjectId = subjectId.ToString(),
        };

        var chunks = await StreamAsync(agent, request, TestContext.Current.CancellationToken);

        chunks.ShouldHaveSingleItem().FinalJson.ShouldBe("project-aware answer");

        await digest.Received(1).BuildDigestAsync(
            Arg.Is<MemoryDigestRequest>(req =>
                req.ScopeKind == MemoryDigestScopes.Project
                && req.SubjectId == subjectId
                && req.Task == "how do we install?"),
            Arg.Any<CancellationToken>());

        var userText = scripted.Calls[0][1].Text;
        userText.ShouldNotBeNull();
        userText!.ShouldContain("# Scoped memory");
        userText.ShouldContain("discovery.finding.install: pnpm install");
        userText.ShouldContain(/*lang=json,strict*/ "{\"runs\": []}");
        userText.IndexOf("# Scoped memory", StringComparison.Ordinal)
            .ShouldBeLessThan(userText.IndexOf(/*lang=json,strict*/ "{\"runs\": []}", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a request with ScopeKind = user and a valid SubjectId, when the loop runs, then IMemoryDigest is called with user scope")]
    public async Task UserScope_CallsMemoryDigestAsync()
    {
        var subjectId = Guid.NewGuid();
        var digest = Substitute.For<IMemoryDigest>();
        digest.BuildDigestAsync(Arg.Any<MemoryDigestRequest>(), Arg.Any<CancellationToken>())
            .Returns("[Standing] discovery.preference.editor: vim");
        var scripted = new ScriptedChatClient([Scripted.Text("user-aware answer")]);
        var agent = Agent(digest, scripted);

        var request = new BrainRequest
        {
            Kind = BrainRequestKindKeys.Answer,
            Task = "which editor?",
            ScopeKind = MemoryDigestScopes.User,
            SubjectId = subjectId.ToString(),
        };

        await StreamAsync(agent, request, TestContext.Current.CancellationToken);

        await digest.Received(1).BuildDigestAsync(
            Arg.Is<MemoryDigestRequest>(req =>
                req.ScopeKind == MemoryDigestScopes.User
                && req.SubjectId == subjectId),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a request with ScopeKind set but SubjectId missing, when the loop runs, then ArgumentException is thrown before any chat round-trip")]
    public async Task ScopeWithoutSubjectId_ThrowsAsync()
    {
        var digest = Substitute.For<IMemoryDigest>();
        var scripted = new ScriptedChatClient([Scripted.Text("never reached")]);
        var agent = Agent(digest, scripted);

        var request = new BrainRequest
        {
            Kind = BrainRequestKindKeys.Answer,
            Task = "what?",
            ScopeKind = MemoryDigestScopes.Project,
            SubjectId = null,
        };

        var exception = await Should.ThrowAsync<ArgumentException>(
                    async () => await StreamAsync(agent, request, TestContext.Current.CancellationToken));

        exception.ParamName.ShouldBe("request");
        exception.Message.ShouldContain("SubjectId");
        await digest.DidNotReceiveWithAnyArgs().BuildDigestAsync(default!, TestContext.Current.CancellationToken);
        scripted.Calls.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a request with ScopeKind set and an unparseable SubjectId, when the loop runs, then ArgumentException is thrown before any chat round-trip")]
    public async Task ScopeWithInvalidSubjectId_ThrowsAsync()
    {
        var digest = Substitute.For<IMemoryDigest>();
        var scripted = new ScriptedChatClient([Scripted.Text("never reached")]);
        var agent = Agent(digest, scripted);

        var request = new BrainRequest
        {
            Kind = BrainRequestKindKeys.Answer,
            Task = "what?",
            ScopeKind = MemoryDigestScopes.Project,
            SubjectId = "not-a-guid",
        };

        var exception = await Should.ThrowAsync<ArgumentException>(
            async () => await StreamAsync(agent, request, TestContext.Current.CancellationToken));

        exception.ParamName.ShouldBe("request");
        exception.Message.ShouldContain("valid Guid");
        await digest.DidNotReceiveWithAnyArgs().BuildDigestAsync(default!, TestContext.Current.CancellationToken);
        scripted.Calls.ShouldBeEmpty();
    }

    private static BrainAgent Agent(IMemoryDigest digest, ScriptedChatClient scripted)
    {
        var options = Options.Create(new BrainOptions());
        return new BrainAgent(
            TimeProvider.System,
            digest,
            new FakeMemoryStore([]),
            new FakeProfileCatalog([new("implement", "Implementer", "writes the code", [], null)]),
            new StubActiveRunCatalog(),
            new StaticModelConfigProvider(),
            new StubExplorerReportReader(),
            new AsyncLocalSubjectScopeAccessor(),
            options,
            new ScriptedChatClientFactory(scripted));
    }

    private static async Task<List<BrainChunk>> StreamAsync(
            BrainAgent agent,
            BrainRequest request,
            CancellationToken cancellationToken)
    {
        var chunks = new List<BrainChunk>();
        await foreach (var chunk in agent.RunAsync(request, cancellationToken))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}
