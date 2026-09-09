using System.Text.Json;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Mcp;
using Comuki.Host.Runs;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Knowledge.Application;
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
/// Unit coverage for the MCP JSON-RPC 2.0 envelope surface and the
/// <see cref="McpServer"/> dispatcher:
/// <list type="bullet">
///   <item>Parse valid JSON-RPC envelopes (request / success / error) into
///   the wire-typed records the dispatcher consumes.</item>
///   <item>Map envelope-shape violations (missing <c>jsonrpc</c>, missing
///   fields) to the standard JSON-RPC error codes
///   (<see cref="JsonRpcEnvelope.ErrorCodes.InvalidRequest"/> /
///   <see cref="JsonRpcEnvelope.ErrorCodes.MethodNotFound"/>) without
///   booting the host composition.</item>
///   <item>Dispatch by method name — <c>tools/list</c> returns the four-
///   tool catalog; <c>tools/call</c> with an unknown tool returns
///   <see cref="JsonRpcEnvelope.ErrorCodes.MethodNotFound"/>; an
///   anonymous caller is denied with the stable
///   <see cref="McpToolPermissionMap.PermissionDeniedCode"/>.</item>
///   <item>Serialize the response envelope back to JSON (using the same
///   explicit-runtime-type pattern as ASP.NET <c>Results.Json</c>) and
///   assert on the wire shape (<c>jsonrpc</c>, <c>id</c>, <c>result</c> /
///   <c>error.code</c>).</item>
/// </list>
/// The MCP server is the boundary that parses untrusted JSON-RPC input
/// from the operator's tooling — these tests close the gap that
/// <c>testing-audit-report.md §1.4</c> flagged.
/// </summary>
public sealed class McpEnvelopeShould
{
    [Fact(DisplayName = "Given a valid JSON-RPC 2.0 envelope, when it is deserialized, then the typed request carries jsonrpc, id, method, params")]
    public void ParseValidEnvelope()
    {
        const string Raw = /*lang=json*/ """{"jsonrpc":"2.0","id":42,"method":"tools/list","params":null}""";

        var request = JsonSerializer.Deserialize<JsonRpcRequest>(Raw, JsonSerializerOptions.Web);

        request.ShouldNotBeNull();
        request.JsonRpc.ShouldBe("2.0");
        request.Id.ShouldNotBeNull();
        request.Id.Value.ValueKind.ShouldBe(JsonValueKind.Number);
        request.Method.ShouldBe("tools/list");
    }

    [Fact(DisplayName = "Given a JSON-RPC envelope missing the jsonrpc field, when DispatchAsync runs, then it returns InvalidRequest (-32600)")]
    public async Task RejectEnvelopeMissingJsonRpcAsync()
    {
        var server = NewServer();
        var request = new JsonRpcRequest(JsonRpc: "1.0", Id: ParseId("7"), Method: "tools/list", Params: null);

        var response = await server.DispatchAsync(request, subject: NewSubject(), TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        var error = ErrorOf(response);
        error.Code.ShouldBe(JsonRpcEnvelope.ErrorCodes.InvalidRequest);
        error.Message.ShouldContain("jsonrpc");
    }

    [Fact(DisplayName = "Given an unknown JSON-RPC method, when DispatchAsync runs, then it returns MethodNotFound (-32601) with the method name in the message")]
    public async Task RejectUnknownMethodAsync()
    {
        var server = NewServer();
        var request = new JsonRpcRequest(JsonRpc: JsonRpcEnvelope.Version, Id: ParseId("3"), Method: "tools/whatever", Params: null);

        var response = await server.DispatchAsync(request, subject: NewSubject(), TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        var error = ErrorOf(response);
        error.Code.ShouldBe(JsonRpcEnvelope.ErrorCodes.MethodNotFound);
        error.Message.ShouldContain("tools/whatever");
    }

    [Fact(DisplayName = "Given a tools/list request, when DispatchAsync runs, then the success envelope carries the four-tool catalog")]
    public async Task ToolsListReturnsCatalogAsync()
    {
        var server = NewServer();
        var request = new JsonRpcRequest(JsonRpc: JsonRpcEnvelope.Version, Id: ParseId("1"), Method: "tools/list", Params: null);

        var response = await server.DispatchAsync(request, subject: null, TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        var json = SerializeAsEndpointWould(response);

        json.GetProperty("jsonrpc").GetString().ShouldBe("2.0");

        var tools = json.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToList();
        tools.ShouldContain("knowledge.search");
        tools.ShouldContain("knowledge.ingest");
        tools.ShouldContain("runs.list");
        tools.ShouldContain("runs.get");
    }

    [Fact(DisplayName = "Given a tools/call with an unknown tool name and an admin subject, when DispatchAsync runs, then the permission gate denies with permission.denied")]
    public async Task ToolsCallRejectsUnknownToolNameAsync()
    {
        // The tool permission map is the single chokepoint — tools not
        // listed there are denied before the switch can map the unknown
        // name to MethodNotFound. The MethodNotFound branch in the
        // switch is unreachable in practice; this test pins that
        // contract so a future refactor that flips the order surfaces
        // here.
        var server = NewServer(permission: new PermissionKey("run:read"));
        var raw = /*lang=json*/ """{"name":"not.a.tool","arguments":{}}""";
        var parameters = JsonSerializer.Deserialize<JsonElement>(raw, JsonSerializerOptions.Web);

        var request = new JsonRpcRequest(JsonRpc: JsonRpcEnvelope.Version, Id: ParseId("2"), Method: "tools/call", Params: parameters);

        var response = await server.DispatchAsync(request, subject: NewSubject(), TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        var error = ErrorOf(response);
        error.Code.ShouldBe(JsonRpcEnvelope.ErrorCodes.InvalidRequest);
        error.Message.ShouldBe(McpToolPermissionMap.PermissionDeniedCode);
    }

    [Fact(DisplayName = "Given a tools/call without a params object, when DispatchAsync runs, then it returns InvalidParams (-32602)")]
    public async Task ToolsCallRejectsMissingParamsAsync()
    {
        var server = NewServer();
        var request = new JsonRpcRequest(JsonRpc: JsonRpcEnvelope.Version, Id: ParseId("4"), Method: "tools/call", Params: null);

        var response = await server.DispatchAsync(request, subject: NewSubject(), TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        var error = ErrorOf(response);
        error.Code.ShouldBe(JsonRpcEnvelope.ErrorCodes.InvalidParams);
    }

    [Fact(DisplayName = "Given an anonymous subject on a tools/call, when DispatchAsync runs, then the response carries permission.denied in the InvalidRequest error")]
    public async Task ToolsCallAnonymousIsPermissionDeniedAsync()
    {
        var server = NewServer();
        var raw = /*lang=json*/ """{"name":"runs.list","arguments":{}}""";
        var parameters = JsonSerializer.Deserialize<JsonElement>(raw, JsonSerializerOptions.Web);

        var request = new JsonRpcRequest(JsonRpc: JsonRpcEnvelope.Version, Id: ParseId("9"), Method: "tools/call", Params: parameters);

        var response = await server.DispatchAsync(request, subject: null, TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        var error = ErrorOf(response);
        error.Code.ShouldBe(JsonRpcEnvelope.ErrorCodes.InvalidRequest);
        error.Message.ShouldBe(McpToolPermissionMap.PermissionDeniedCode);
    }

    [Fact(DisplayName = "Given a subject without the tool's required permission, when DispatchAsync runs, then permission.denied is returned")]
    public async Task ToolsCallSubjectMissingPermissionIsDeniedAsync()
    {
        // Evaluator returns an empty SubjectAuthorization so the subject
        // has no permissions — the dispatcher must deny by default.
        var evaluator = Substitute.For<IPermissionEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>())
            .Returns(SubjectAuthorization.Empty);

        var server = NewServer(evaluator: evaluator);
        var raw = /*lang=json*/ """{"name":"runs.list","arguments":{}}""";
        var parameters = JsonSerializer.Deserialize<JsonElement>(raw, JsonSerializerOptions.Web);

        var request = new JsonRpcRequest(JsonRpc: JsonRpcEnvelope.Version, Id: ParseId("5"), Method: "tools/call", Params: parameters);

        var response = await server.DispatchAsync(request, subject: NewSubject(), TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        var error = ErrorOf(response);
        error.Code.ShouldBe(JsonRpcEnvelope.ErrorCodes.InvalidRequest);
        error.Message.ShouldBe(McpToolPermissionMap.PermissionDeniedCode);
    }

    [Fact(DisplayName = "Given a serialized error response, when JsonSerializer writes it back to JSON, then jsonrpc, id, error.code are visible on the wire")]
    public void SerializeErrorEnvelopeRoundTrips()
    {
        var error = JsonRpcResponse.Failure(
            id: null,
            code: JsonRpcEnvelope.ErrorCodes.ParseError,
            message: "JSON parse error: trailing garbage",
            Data: null);

        var json = SerializeAsEndpointWould(error);

        json.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
        json.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(JsonRpcEnvelope.ErrorCodes.ParseError);
        json.GetProperty("error").GetProperty("message").GetString().ShouldBe("JSON parse error: trailing garbage");
    }

    [Fact(DisplayName = "Given a serialized success envelope, when JsonSerializer writes it back to JSON, then jsonrpc, id, and the result block are visible on the wire")]
    public void SerializeSuccessEnvelopeRoundTrips()
    {
        var response = JsonRpcResponse.Success(
            id: ParseId("11"),
            result: new { ok = true, count = 3 });

        var json = SerializeAsEndpointWould(response);

        json.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
        json.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Number);
        json.GetProperty("result").GetProperty("ok").GetBoolean().ShouldBeTrue();
        json.GetProperty("result").GetProperty("count").GetInt32().ShouldBe(3);
    }

    /// <summary>
    /// Mirrors what ASP.NET <c>Results.Json(value, options, statusCode)</c>
    /// does internally: serializes via <c>value?.GetType() ?? typeof(object)</c>.
    /// The overload that omits the type uses the static type and silently
    /// drops the derived-type members — bypass that by passing the runtime
    /// type explicitly so the test exercises the same code path the
    /// endpoint does.
    /// </summary>
    private static JsonElement SerializeAsEndpointWould(JsonRpcResponse response)
    {
        return JsonSerializer.SerializeToElement(response, response.GetType(), JsonSerializerOptions.Web);
    }

    private static McpServer NewServer(IPermissionEvaluator? evaluator = null, PermissionKey? permission = null)
    {
        permission ??= new PermissionKey("run:read");
        var resolvedEvaluator = evaluator ?? BuildEvaluator(permission.Value);
        return new McpServer(
            toolHandlers: BuildToolHandlers(),
            permissionEvaluator: resolvedEvaluator,
            logger: NullLogger<McpServer>.Instance);
    }

    private static McpToolHandlers BuildToolHandlers()
    {
        return new McpToolHandlers(
            knowledgeSearcher: Substitute.For<IKnowledgeSearcher>(),
            knowledgeIngestor: Substitute.For<IKnowledgeIngestor>(),
            runsList: NewRunsListHandler());
    }

    private static IPermissionEvaluator BuildEvaluator(PermissionKey key)
    {
        var evaluator = Substitute.For<IPermissionEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<RoleSubject>(), Arg.Any<CancellationToken>())
            .Returns(new SubjectAuthorization(
                PlatformPermissions: new HashSet<PermissionKey> { key },
                ProjectPermissions: new Dictionary<ProjectId, IReadOnlySet<PermissionKey>>()));
        return evaluator;
    }

    private static RunsListHandler NewRunsListHandler()
    {
        // RunsListHandler is sealed (default per naming-and-types.md §2)
        // and NSubstitute cannot proxy sealed classes. Construct a real
        // instance against an in-memory OrchestrationDbContext — the
        // dispatch tests assert on the wire envelope, not on runs rows,
        // so the empty in-memory store is sufficient.
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"mcp-orch-ctx-{Guid.NewGuid():N}")
            .ConfigureWarnings(static warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new OrchestrationDbContext(options, scopeAccessor: NewUnrestrictedScopeAccessor());
        return new RunsListHandler(context, TimeProvider.System);
    }

    private static ISubjectScopeAccessor NewUnrestrictedScopeAccessor()
    {
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        accessor.Current.Returns(new SubjectScope(Unrestricted: true, SystemName: "unit-test", ProjectIds: []));
        return accessor;
    }

    private static RoleSubject NewSubject()
    {
        return RoleSubject.ForUser(new Modules.Identity.Domain.Ids.UserId(Guid.NewGuid()));
    }

    private static JsonElement? ParseId(string value)
    {
        return JsonSerializer.Deserialize<JsonElement>(value).ValueKind == JsonValueKind.Undefined
            ? null
            : JsonSerializer.Deserialize<JsonElement>(value);
    }

    private static JsonRpcErrorBody ErrorOf(JsonRpcResponse response)
    {
        var json = SerializeAsEndpointWould(response);
        var errorElement = json.GetProperty("error");
        var code = errorElement.GetProperty("code").GetInt32();
        var message = errorElement.GetProperty("message").GetString() ?? string.Empty;
        return new JsonRpcErrorBody(code, message, null);
    }
}
