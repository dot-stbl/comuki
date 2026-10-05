using System.Text.Json;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Mcp;
using Comuki.Host.Runs;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Subjects;
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
/// Per-tool permission map coverage for the four observability.* tools.
/// The map requires <c>observability:read</c> for every observability tool;
/// the worker gate (separate test surface) keeps the four tools out of
/// the worker set so they are denied for worker-token callers before the
/// permission map runs. This file fixes the subject-side policy.
///
/// The four tools under test (per <c>specs/observability/spec.md</c>):
///   <c>observability.logs.search</c>,
///   <c>observability.metrics.query</c>,
///   <c>observability.logs.context</c>,
///   <c>observability.metrics.series</c>.
/// </summary>
public sealed class McpToolPermissionMapShould
{
    private static readonly IReadOnlyList<string> observabilityTools =
    [
        "observability.logs.search",
        "observability.metrics.query",
        "observability.logs.context",
        "observability.metrics.series",
    ];

    [Fact(DisplayName = "Given a subject without observability:read, when the map evaluates any observability.* tool, then the call is denied")]
    public async Task SubjectWithoutObservabilityReadIsDeniedAllObservabilityToolsAsync()
    {
        var evaluator = NewEvaluator(granted: [new("run:read")]);

        foreach (var tool in observabilityTools)
        {
            var allowed = await McpToolPermissionMap.IsAllowedAsync(
                tool, NewSubject(), evaluator, TestContext.Current.CancellationToken);

            allowed.ShouldBeFalse(
                $"a subject without observability:read must NOT invoke {tool}");
        }

        await evaluator.Received(observabilityTools.Count)
            .EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a subject with observability:read, when the map evaluates any observability.* tool, then the call is allowed")]
    public async Task SubjectWithObservabilityReadIsAllowedAllObservabilityToolsAsync()
    {
        var evaluator = NewEvaluator(granted: [new("observability:read")]);

        foreach (var tool in observabilityTools)
        {
            var allowed = await McpToolPermissionMap.IsAllowedAsync(
                tool, NewSubject(), evaluator, TestContext.Current.CancellationToken);

            allowed.ShouldBeTrue(
                $"a subject with observability:read must invoke {tool}");
        }
    }

    [Fact(DisplayName = "Given an anonymous (null) subject, when the map evaluates any observability.* tool, then the call is denied before the evaluator runs")]
    public async Task AnonymousSubjectIsDeniedObservabilityToolsAsync()
    {
        var evaluator = NewEvaluator(granted: [new("observability:read")]);

        foreach (var tool in observabilityTools)
        {
            var allowed = await McpToolPermissionMap.IsAllowedAsync(
                tool, subject: null, evaluator, TestContext.Current.CancellationToken);

            allowed.ShouldBeFalse(
                $"anonymous subjects must never invoke {tool}");
        }

        await evaluator.DidNotReceive().EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Stable failure code surfaced to JSON-RPC clients when the gate denies.</summary>
    [Fact(DisplayName = "Given the map denies, the PermissionDeniedCode constant is the stable 'permission.denied' string clients branch on")]
    public void PermissionDeniedCodeIsStable()
    {
        McpToolPermissionMap.PermissionDeniedCode.ShouldBe("permission.denied");
    }

    /// <summary>
    /// The worker-side twin of the subject map: every observability.* tool
    /// is subject-side only — workers do NOT carry <c>observability:read</c>,
    /// and the worker tool gate's worker-set excludes them all, so the
    /// full dispatch path denies with the same <c>permission.denied</c>
    /// code as a missing permission. This test exercises that contract
    /// end-to-end through the McpServer dispatcher so the wire shape
    /// (HTTP error code + JSON-RPC message) stays locked.
    /// </summary>
    [Theory(DisplayName = "Given a worker, when the dispatcher invokes an observability.* tool, then the worker gate denies with permission.denied")]
    [InlineData("observability.logs.search", /*lang=json,strict*/ """{"query":"level:error"}""")]
    [InlineData("observability.metrics.query", /*lang=json,strict*/ """{"query":"up","start":1700000000000,"end":1700000015000,"step":15000}""")]
    [InlineData("observability.logs.context", /*lang=json,strict*/ """{"traceId":"00112233445566778899aabbccddeeff"}""")]
    [InlineData("observability.metrics.series", /*lang=json,strict*/ """{"match":"{job=\"comuki-orchestrator\"}"}""")]
    public async Task WorkerIsDeniedObservabilityToolsAsync(string toolName, string argumentsJson)
    {
        var server = NewServerWithEvaluator(NewEvaluator(granted: [new("observability:read")]));
        var caller = new McpCaller(Worker: new McpWorkerCaller(WorkerId.New(), Guid.NewGuid()));

        var parameters = JsonSerializer.Deserialize<JsonElement>($$"""{"name":"{{toolName}}","arguments":{{argumentsJson}}}""", JsonSerializerOptions.Web);
        var request = new JsonRpcRequest(
            JsonRpc: JsonRpcEnvelope.Version,
            Id: JsonSerializer.Deserialize<JsonElement>("1"),
            Method: "tools/call",
            Params: parameters);

        var response = await server.DispatchAsync(request, caller, TestContext.Current.CancellationToken);

        var json = JsonSerializer.SerializeToElement(response.ShouldNotBeNull(), response.GetType(), JsonSerializerOptions.Web);
        var errorElement = json.GetProperty("error");
        errorElement.GetProperty("code").GetInt32().ShouldBe(JsonRpcEnvelope.ErrorCodes.InvalidRequest);
        errorElement.GetProperty("message").GetString().ShouldBe(McpToolPermissionMap.PermissionDeniedCode);
    }

    private static McpServer NewServerWithEvaluator(IPermissionEvaluator evaluator)
    {
        return new McpServer(
            toolHandlers: new McpToolHandlers(
                knowledgeSearcher: Substitute.For<Modules.Knowledge.Application.IKnowledgeSearcher>(),
                knowledgeIngestor: Substitute.For<Modules.Knowledge.Application.IKnowledgeIngestor>(),
                memoryStore: Substitute.For<Modules.Memory.Application.Ports.IMemoryStore>(),
                noteRateLimiter: new WorkerNoteRateLimiter(TimeProvider.System),
                learningCandidates: Substitute.For<Modules.Memory.Application.Learning.ILearningCandidateStore>(),
                suggestRateLimiter: new WorkerSuggestRateLimiter(TimeProvider.System),
                runsList: NewRunsListHandler(),
                logsQueryClient: Substitute.For<Modules.Observability.Application.Ports.IVictoriaLogsQueryClient>(),
                metricsQueryClient: Substitute.For<Modules.Observability.Application.Ports.IVictoriaMetricsQueryClient>(),
                clock: TimeProvider.System),
            permissionEvaluator: evaluator,
            logger: NullLogger<McpServer>.Instance);
    }

    private static RunsListHandler NewRunsListHandler()
    {
        // RunsListHandler is sealed (default per naming-and-types.md §2)
        // and NSubstitute cannot proxy sealed classes. Construct a real
        // instance against an in-memory OrchestrationDbContext — the
        // observability gate tests assert on the gate denial, not on
        // the RunsList body, so the empty in-memory store is sufficient.
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"mcp-permission-map-orch-ctx-{Guid.NewGuid():N}")
            .ConfigureWarnings(static warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        accessor.Current.Returns(new SubjectScope(Unrestricted: true, SystemName: "unit-test", ProjectIds: []));
        var context = new OrchestrationDbContext(options, scopeAccessor: accessor);
        return new RunsListHandler(context, TimeProvider.System);
    }

    private static IPermissionEvaluator NewEvaluator(IReadOnlyList<PermissionKey> granted)
    {
        var evaluator = Substitute.For<IPermissionEvaluator>();
        var grantedSet = new HashSet<PermissionKey>(granted);
        evaluator.EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>())
            .Returns(new SubjectAuthorization(
                PlatformPermissions: grantedSet,
                ProjectPermissions: new Dictionary<ProjectId, IReadOnlySet<PermissionKey>>()));
        return evaluator;
    }

    private static RoleSubject NewSubject()
    {
        return RoleSubject.ForUser(new Modules.Identity.Domain.Ids.UserId(Guid.NewGuid()));
    }
}
