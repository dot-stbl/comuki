using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Smoke;

/// <summary>
/// MCP JSON-RPC 2.0 endpoint coverage (S10 #9) + per-tool permission
/// gate (security audit A01-1):
/// <list type="bullet">
///   <item><c>tools/list</c> returns the four-tool catalog
///     (<c>knowledge.search</c>, <c>knowledge.ingest</c>, <c>runs.list</c>,
///     <c>runs.get</c>). Anonymous-friendly — the gate sits on
///     <c>tools/call</c>, not the catalog.</item>
///   <item><c>tools/call</c> with <c>name=runs.list</c> returns the runs
///     page shape (empty <c>items</c> array in the smoke fixture) when
///     the caller carries <c>run:read</c>.</item>
///   <item><c>tools/call</c> with an anonymous cookie-less client surfaces
///     a JSON-RPC <c>InvalidRequest</c> (-32600) carrying
///     <c>permission.denied</c> — the dispatcher is deny-by-default.</item>
///   <item>A JSON-RPC envelope missing the <c>jsonrpc</c> field surfaces
///     the standard JSON-RPC <c>InvalidRequest</c> error code (-32600)
///     in the response envelope. The HTTP status stays 200 — JSON-RPC
///     errors ride inside the success envelope; only ParseError
///     (malformed JSON body) returns 400.</item>
/// </list>
/// </summary>
public sealed class McpShould(SmokeHostServer server) : IClassFixture<SmokeHostServer>
{
    private readonly SmokeHostServer server = server;

    [Fact(DisplayName = "Given a JSON-RPC 2.0 request, when tools/list, then 200 with the four MCP tool names")]
    public async Task ToolsListReturnsCatalogAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = server.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/mcp",
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = (object?)null,
            },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // JSON-RPC 2.0 §"Response" — every reply carries "jsonrpc":"2.0".
        payload.GetProperty("jsonrpc").GetString().ShouldBe("2.0");

        var tools = payload.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToList();
        tools.ShouldContain("knowledge.search");
        tools.ShouldContain("knowledge.ingest");
        tools.ShouldContain("runs.list");
        tools.ShouldContain("runs.get");
    }

    [Fact(DisplayName = "Given an authenticated subject with run:read, when tools/call name=runs.list, then 200 with the runs page shape")]
    public async Task RunsListToolReturnsRunsArrayAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await server.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/mcp",
            new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "runs.list",
                    arguments = new { },
                },
            },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        payload.GetProperty("jsonrpc").GetString().ShouldBe("2.0");

        var result = payload.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeFalse();

        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.ShouldNotBeNullOrWhiteSpace();

        var page = JsonSerializer.Deserialize<JsonElement>(text);
        page.GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact(DisplayName = "Given an anonymous caller, when tools/call name=runs.list, then JSON-RPC InvalidRequest with permission.denied")]
    public async Task AnonymousToolCallIsDeniedAsync()
    {
        // Security audit A01-1: the MCP dispatcher is the single
        // chokepoint for every tool call. An anonymous caller (no
        // cookie / api-key principal) carries no subject — the
        // permission gate denies by default and the wire carries
        // JSON-RPC InvalidRequest (-32600) + the stable
        // "permission.denied" code so clients can branch on it.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = server.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/mcp",
            new
            {
                jsonrpc = "2.0",
                id = 7,
                method = "tools/call",
                @params = new
                {
                    name = "runs.list",
                    arguments = new { },
                },
            },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        payload.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
        payload.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32600);
        payload.GetProperty("error").GetProperty("message").GetString().ShouldBe("permission.denied");
    }

    [Fact(DisplayName = "Given a JSON-RPC envelope missing the jsonrpc field, when POST /api/v1/mcp, then the JSON-RPC InvalidRequest error envelope comes back")]
    public async Task MissingJsonrpcReturnsInvalidRequestAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = server.CreateAnonymousClient();

        // Wire format: drop the jsonrpc field; the dispatcher must
        // surface JSON-RPC InvalidRequest (-32600). The HTTP status
        // stays 200 — JSON-RPC errors ride inside the success envelope
        // shape; only ParseError (malformed JSON body) returns 400.
        var raw = /*lang=json,strict*/ """{"id":3,"method":"tools/list"}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/mcp")
        {
            Content = new StringContent(raw, System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        payload.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
        payload.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32600);
    }
}
