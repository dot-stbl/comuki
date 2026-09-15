using System.Text.Json;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Mcp;
using Comuki.Host.Runs;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Mcp;

/// <summary>
/// Unit coverage for the worker-caller surface of the MCP dispatcher:
/// the swarm worker's project-bound tools (memory.recall, memory.note,
/// knowledge.search), the scope enforcement (project derived from the
/// lease, never from a client-supplied argument), the worker tool gate
/// (workers get nothing beyond their set) and the memory.note rate
/// limiter. Subject-caller behaviour of the original four tools stays
/// in <see cref="McpEnvelopeShould"/>.
/// </summary>
public sealed class McpWorkerToolsShould
{
    [Fact(DisplayName = "Given a worker with an active work item, when tools/call memory.recall, then the store is searched in the worker's project scope")]
    public async Task MemoryRecallSearchesWorkerProjectScopeAsync()
    {
        var projectId = Guid.NewGuid();
        var memoryStore = Substitute.For<IMemoryStore>();
        memoryStore.SearchAsync(Arg.Any<MemoryFactQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                NewFact(projectId, MemoryFactKind.Standing, "auth.pattern", "JWT validated by the platform bearer scheme"),
            ]);
        var server = NewServer(memoryStore: memoryStore);

        var response = await DispatchToolAsync(server, NewWorkerCaller(projectId), "memory.recall", /*lang=json,strict*/ """{"query":"auth"}""");

        var result = ResultOf(response);
        result.IsError.ShouldBeFalse();
        await memoryStore.Received(1).SearchAsync(
            Arg.Is<MemoryFactQuery>(query =>
                query.Scope == MemoryScope.Project
                && query.SubjectId == projectId.ToString()
                && query.Limit == 5),
            Arg.Any<CancellationToken>());

        TextOf(result).ShouldContain("auth.pattern");
        TextOf(result).ShouldContain("JWT validated by the platform bearer scheme");
    }

    [Fact(DisplayName = "Given a worker's memory.recall with topK 99, when dispatched, then topK is clamped to the 20-fact ceiling")]
    public async Task MemoryRecallClampsTopKAsync()
    {
        var memoryStore = Substitute.For<IMemoryStore>();
        memoryStore.SearchAsync(Arg.Any<MemoryFactQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var server = NewServer(memoryStore: memoryStore);

        var response = await DispatchToolAsync(server, NewWorkerCaller(Guid.NewGuid()), "memory.recall", /*lang=json,strict*/ """{"query":"build","topK":99}""");

        await memoryStore.Received(1).SearchAsync(
            Arg.Is<MemoryFactQuery>(static query => query.Limit == McpToolHandlers.RecallMaxTopK),
            Arg.Any<CancellationToken>());
        TextOf(ResultOf(response)).ShouldContain("no memory facts");
    }

    [Fact(DisplayName = "Given a worker's memory.note, when dispatched, then the fact is written to the worker's project as a run-source standing fact")]
    public async Task MemoryNoteWritesProjectScopedRunFactAsync()
    {
        var projectId = Guid.NewGuid();
        var workerId = WorkerId.New();
        var memoryStore = Substitute.For<IMemoryStore>();
        memoryStore.WriteAsync(Arg.Any<MemoryFactWrite>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => NewFact(projectId, ((MemoryFactWrite)callInfo[0]).Kind, ((MemoryFactWrite)callInfo[0]).TopicKey));
        var server = NewServer(memoryStore: memoryStore);

        var response = await DispatchToolAsync(
            server,
            new McpCaller(Worker: new McpWorkerCaller(workerId, projectId)),
            "memory.note",
            /*lang=json,strict*/ """{"topic":"Build.Gotcha","text":"bun install, never npm — lockfile is bun.lock"}""");

        TextOf(ResultOf(response)).ShouldContain("remembered 'build.gotcha' (standing)");
        await memoryStore.Received(1).WriteAsync(
            Arg.Is<MemoryFactWrite>(write =>
                write.Scope == MemoryScope.Project
                && write.SubjectId == projectId.ToString()
                && write.Kind == MemoryFactKind.Standing
                && write.TopicKey == "Build.Gotcha"
                && write.Text == "bun install, never npm — lockfile is bun.lock"
                && write.Source == MemorySource.Run
                && write.CreatedBy == $"worker:{workerId.Value}"),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a worker's memory.note with ephemeral=true, when dispatched, then the fact kind is ephemeral")]
    public async Task MemoryNoteEphemeralFlagSelectsEphemeralKindAsync()
    {
        var memoryStore = Substitute.For<IMemoryStore>();
        memoryStore.WriteAsync(Arg.Any<MemoryFactWrite>(), Arg.Any<CancellationToken>())
            .Returns(static callInfo => NewFact(Guid.NewGuid(), ((MemoryFactWrite)callInfo[0]).Kind, ((MemoryFactWrite)callInfo[0]).TopicKey));
        var server = NewServer(memoryStore: memoryStore);

        var response = await DispatchToolAsync(server, NewWorkerCaller(Guid.NewGuid()), "memory.note", /*lang=json,strict*/ """{"topic":"flaky.test","text":"test_xyz fails on cold cache","ephemeral":true}""");

        TextOf(ResultOf(response)).ShouldContain("ephemeral");
        await memoryStore.Received(1).WriteAsync(
            Arg.Is<MemoryFactWrite>(static write => write.Kind == MemoryFactKind.Ephemeral),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a worker's memory.note with an empty topic, when dispatched, then InvalidParams comes back and nothing is written")]
    public async Task MemoryNoteRejectsEmptyTopicAsync()
    {
        var memoryStore = Substitute.For<IMemoryStore>();
        var server = NewServer(memoryStore: memoryStore);

        var response = await DispatchToolAsync(server, NewWorkerCaller(Guid.NewGuid()), "memory.note", /*lang=json,strict*/ """{"topic":"","text":"x"}""");

        ErrorOf(response).Code.ShouldBe(JsonRpcEnvelope.ErrorCodes.InvalidParams);
        await memoryStore.DidNotReceive().WriteAsync(Arg.Any<MemoryFactWrite>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a worker at the note rate limit, when memory.note is called once more, then the tool answers an error and skips the write")]
    public async Task MemoryNoteRateLimitedWorkerIsRejectedAsync()
    {
        var memoryStore = Substitute.For<IMemoryStore>();
        memoryStore.WriteAsync(Arg.Any<MemoryFactWrite>(), Arg.Any<CancellationToken>())
            .Returns(static callInfo => NewFact(Guid.NewGuid(), ((MemoryFactWrite)callInfo[0]).Kind, ((MemoryFactWrite)callInfo[0]).TopicKey));
        var server = NewServer(memoryStore: memoryStore);
        var caller = NewWorkerCaller(Guid.NewGuid());

        for (var admitted = 0; admitted < WorkerNoteRateLimiter.Limit; admitted++)
        {
            await DispatchToolAsync(server, caller, "memory.note", /*lang=json,strict*/ """{"topic":"t","text":"x"}""");
        }

        var response = await DispatchToolAsync(server, caller, "memory.note", /*lang=json,strict*/ """{"topic":"t","text":"x"}""");

        var result = ResultOf(response);
        result.IsError.ShouldBeTrue();
        TextOf(result).ShouldContain("rate limit");
        await memoryStore.Received(WorkerNoteRateLimiter.Limit).WriteAsync(Arg.Any<MemoryFactWrite>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a worker's knowledge.search with a foreign projectId argument, when dispatched, then the search runs scoped to the worker's own project only")]
    public async Task KnowledgeSearchForcesWorkerProjectScopeAsync()
    {
        var workerProject = Guid.NewGuid();
        var foreignProject = Guid.NewGuid();
        var knowledgeSearcher = Substitute.For<IKnowledgeSearcher>();
        knowledgeSearcher.SearchAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var server = NewServer(knowledgeSearcher: knowledgeSearcher);

        var response = await DispatchToolAsync(
            server,
            NewWorkerCaller(workerProject),
            "knowledge.search",
            /*lang=json,strict*/ $$"""{"query":"deploy pipeline","projectId":"{{foreignProject}}"}""");

        await knowledgeSearcher.Received(1).SearchAsync(
            "deploy pipeline",
            workerProject,
            Arg.Any<int>(),
            Arg.Any<float>(),
            Arg.Any<CancellationToken>());
        ResultOf(response).IsError.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a worker calling runs.list, when dispatched, then the worker gate denies with permission.denied")]
    public async Task WorkerIsDeniedBeyondItsToolSetAsync()
    {
        var server = NewServer();

        var response = await DispatchToolAsync(server, NewWorkerCaller(Guid.NewGuid()), "runs.list", /*lang=json,strict*/ """{}""");

        var error = ErrorOf(response);
        error.Code.ShouldBe(JsonRpcEnvelope.ErrorCodes.InvalidRequest);
        error.Message.ShouldBe(McpToolPermissionMap.PermissionDeniedCode);
    }

    [Fact(DisplayName = "Given a worker with no active work item, when memory.recall is called, then the gate denies — no project scope exists")]
    public async Task WorkerWithoutActiveItemIsDeniedAsync()
    {
        var memoryStore = Substitute.For<IMemoryStore>();
        var server = NewServer(memoryStore: memoryStore);

        var response = await DispatchToolAsync(
            server,
            new McpCaller(Worker: new McpWorkerCaller(WorkerId.New(), ProjectId: null)),
            "memory.recall",
            /*lang=json,strict*/ """{"query":"anything"}""");

        ErrorOf(response).Message.ShouldBe(McpToolPermissionMap.PermissionDeniedCode);
        await memoryStore.DidNotReceive().SearchAsync(Arg.Any<MemoryFactQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a cookie/api-key subject calling memory.recall, when dispatched, then the permission map denies — worker tools are worker-only")]
    public async Task SubjectIsDeniedWorkerToolsAsync()
    {
        var memoryStore = Substitute.For<IMemoryStore>();
        var server = NewServer(memoryStore: memoryStore);

        var response = await DispatchToolAsync(server, new McpCaller(Subject: NewSubject()), "memory.recall", /*lang=json,strict*/ """{"query":"anything"}""");

        ErrorOf(response).Message.ShouldBe(McpToolPermissionMap.PermissionDeniedCode);
        await memoryStore.DidNotReceive().SearchAsync(Arg.Any<MemoryFactQuery>(), Arg.Any<CancellationToken>());
    }

    private static Task<JsonRpcResponse?> DispatchToolAsync(McpServer server, McpCaller caller, string toolName, string argumentsJson)
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>($$"""{"name":"{{toolName}}","arguments":{{argumentsJson}}}""", JsonSerializerOptions.Web);
        var request = new JsonRpcRequest(JsonRpc: JsonRpcEnvelope.Version, Id: ParseId("1"), Method: "tools/call", Params: parameters);

        return server.DispatchAsync(request, caller, TestContext.Current.CancellationToken);
    }

    private static McpServer NewServer(
        IKnowledgeSearcher? knowledgeSearcher = null,
        IMemoryStore? memoryStore = null)
    {
        return new McpServer(
            toolHandlers: new McpToolHandlers(
                knowledgeSearcher: knowledgeSearcher ?? Substitute.For<IKnowledgeSearcher>(),
                knowledgeIngestor: Substitute.For<IKnowledgeIngestor>(),
                memoryStore: memoryStore ?? Substitute.For<IMemoryStore>(),
                noteRateLimiter: new WorkerNoteRateLimiter(TimeProvider.System),
                runsList: NewRunsListHandler(),
                clock: TimeProvider.System),
            permissionEvaluator: NewEvaluator(),
            logger: NullLogger<McpServer>.Instance);
    }

    private static IPermissionEvaluator NewEvaluator()
    {
        var evaluator = Substitute.For<IPermissionEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>())
            .Returns(new SubjectAuthorization(
                PlatformPermissions: new HashSet<PermissionKey> { new("run:read") },
                ProjectPermissions: new Dictionary<ProjectId, IReadOnlySet<PermissionKey>>()));
        return evaluator;
    }

    private static McpCaller NewWorkerCaller(Guid projectId)
    {
        return new McpCaller(Worker: new McpWorkerCaller(WorkerId.New(), projectId));
    }

    private static RunsListHandler NewRunsListHandler()
    {
        // RunsListHandler is sealed (default per naming-and-types.md §2)
        // and NSubstitute cannot proxy sealed classes. Construct a real
        // instance against an in-memory OrchestrationDbContext — the
        // worker-tool tests assert on their own ports, not on runs rows,
        // so the empty in-memory store is sufficient.
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"mcp-worker-orch-ctx-{Guid.NewGuid():N}")
            .ConfigureWarnings(static warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        accessor.Current.Returns(new SubjectScope(Unrestricted: true, SystemName: "unit-test", ProjectIds: []));
        var context = new OrchestrationDbContext(options, scopeAccessor: accessor);
        return new RunsListHandler(context, TimeProvider.System);
    }

    private static MemoryFactView NewFact(Guid projectId, MemoryFactKind kind, string topicKey, string text = "fact text")
    {
        return new MemoryFactView(
            MemoryFactId.New(),
            MemoryScope.Project,
            projectId.ToString(),
            kind,
            topicKey,
            text,
            MemorySource.Run,
            "worker:test",
            DateTimeOffset.UtcNow);
    }

    private static RoleSubject NewSubject()
    {
        return RoleSubject.ForUser(new Modules.Identity.Domain.Ids.UserId(Guid.NewGuid()));
    }

    private static JsonElement? ParseId(string value)
    {
        return JsonSerializer.Deserialize<JsonElement>(value);
    }

    private static ToolResult ResultOf(JsonRpcResponse? response)
    {
        var json = JsonSerializer.SerializeToElement(response.ShouldNotBeNull(), response.GetType(), JsonSerializerOptions.Web);
        return json.GetProperty("result").Deserialize<ToolResult>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("tool result missing");
    }

    private static string TextOf(ToolResult result)
    {
        return result.Content.Single().Text;
    }

    private static JsonRpcErrorBody ErrorOf(JsonRpcResponse? response)
    {
        var json = JsonSerializer.SerializeToElement(response.ShouldNotBeNull(), response.GetType(), JsonSerializerOptions.Web);
        var errorElement = json.GetProperty("error");
        return new JsonRpcErrorBody(
            errorElement.GetProperty("code").GetInt32(),
            errorElement.GetProperty("message").GetString() ?? string.Empty,
            null);
    }
}
