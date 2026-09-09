using System.Text.Json;

namespace Comuki.Host.Mcp;

/// <summary>
/// Static catalogue of MCP tools exposed by <see cref="McpServer"/>.
/// Extracted from <c>McpServer.ListToolsAsync</c> so the dispatcher class
/// holds only orchestration (per <c>class-layout-and-tooling.md §1a</c>).
/// The shape follows the JSON-RPC 2.0 <c>tools/list</c> convention — four
/// tools, each with a JSON Schema for its arguments. The catalogue is
/// currently static; a future async registry lookup (with
/// <c>cancellationToken</c>) can replace this without changing the
/// dispatcher signature.
/// </summary>
internal static class McpToolCatalog
{
    /// <summary>Lists every tool the MCP server exposes, in JSON-RPC <c>tools/list</c> shape.</summary>
    /// <param name="id">JSON-RPC request id; passed through to the response.</param>
    public static JsonRpcResponse List(JsonElement? id)
    {
        var tools = new object[]
        {
            new
            {
                name = "knowledge.search",
                description = "Search the knowledge base by semantic similarity (pgvector cosine distance).",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["query"] = new { type = "string", description = "Natural-language search query." },
                        ["projectId"] = new { type = "string", description = "Optional project scope; omit for global." },
                        ["topK"] = new { type = "integer", description = "Maximum number of hits (default 5)." },
                        ["minSimilarity"] = new { type = "number", description = "Cosine-similarity floor in [0.0, 1.0] (default 0.5)." },
                    },
                    required = new[] { "query" },
                },
            },
            new
            {
                name = "knowledge.ingest",
                description = "Ingest a source of knowledge content (chunked + embedded into pgvector).",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["title"] = new { type = "string", description = "Human-readable title." },
                        ["source"] = new { type = "string", description = "Origin kind — git | upload | url." },
                        ["sourceRef"] = new { type = "string", description = "Origin pointer (URL, commit, blob id)." },
                        ["mimeType"] = new { type = "string", description = "Detected MIME type (text/markdown, …)." },
                        ["text"] = new { type = "string", description = "Raw text the worker chunks + embeds." },
                        ["projectId"] = new { type = "string", description = "Optional project scope." },
                    },
                    required = new[] { "title", "source", "sourceRef", "mimeType", "text" },
                },
            },
            new
            {
                name = "runs.list",
                description = "List runs — optional projectId + status filter, paged.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["projectId"] = new { type = "string", description = "Project scope (Guid string)." },
                        ["status"] = new { type = "string", description = "Status filter (queued | waiting | running | …)." },
                    },
                },
            },
            new
            {
                name = "runs.get",
                description = "Fetch one run by id.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["runId"] = new { type = "string", description = "Run id (Guid)." },
                    },
                    required = new[] { "runId" },
                },
            },
        };

        return JsonRpcResponse.Success(id, new { tools });
    }
}
