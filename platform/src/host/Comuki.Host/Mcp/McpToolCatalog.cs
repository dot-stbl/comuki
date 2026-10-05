using System.Text.Json;

namespace Comuki.Host.Mcp;

/// <summary>
/// Static catalogue of MCP tools exposed by <see cref="McpServer"/>.
/// Extracted from <c>McpServer.ListToolsAsync</c> so the dispatcher class
/// holds only orchestration (per <c>class-layout-and-tooling.md §1a</c>).
/// The shape follows the JSON-RPC 2.0 <c>tools/list</c> convention — eleven
/// tools, each with a JSON Schema for its arguments. The catalogue is
/// static and caller-agnostic; who may call what is the gates' job
/// (<see cref="McpToolPermissionMap"/> for subjects,
/// <see cref="McpWorkerToolGate"/> for workers).
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
                name = "memory.recall",
                description = "Search saved facts and decisions for this project. Use to recall prior decisions, architectural constraints, or gotchas other workers recorded.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["query"] = new { type = "string", description = "What to look for." },
                        ["topK"] = new { type = "integer", description = "Maximum facts returned (default 5, max 20)." },
                    },
                    required = new[] { "query" },
                },
            },
            new
            {
                name = "memory.note",
                description = "Save a durable observation for future workers on this project. Use sparingly — only architectural findings, gotchas, or decisions worth remembering.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["topic"] = new { type = "string", description = "Short key like 'auth.pattern' or 'build.gotcha' — same topic overwrites." },
                        ["text"] = new { type = "string", description = "The fact or observation (4000 characters max)." },
                        ["ephemeral"] = new { type = "boolean", description = "True expires the note after the platform's ephemeral horizon (default false = standing)." },
                    },
                    required = new[] { "topic", "text" },
                },
            },
            new
            {
                name = "learning.suggest",
                description = "Propose a rule or pattern worth remembering across ALL future runs. Use when you discover a non-obvious insight: a build gotcha, an architectural constraint, a recurring pattern. The suggestion goes to human review before becoming a rule.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["topic"] = new { type = "string", description = "Short key like 'build.dotnet' or 'testing.xunit'." },
                        ["observation"] = new { type = "string", description = "What you observed (the evidence)." },
                        ["proposedRule"] = new { type = "string", description = "The rule to add, stated as an imperative." },
                    },
                    required = new[] { "topic", "observation", "proposedRule" },
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
            new
            {
                name = "observability.logs.search",
                description = "Search VictoriaLogs with a LogsQL query over the platform's log stream. The ambient OTel trace id is auto-appended unless an explicit traceId is provided; a window pair (from, to) narrows the search to a time range; limit caps the row count.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["query"] = new { type = "string", description = "LogsQL expression (the text after _stream: or a free clause)." },
                        ["from"] = new { type = "string", description = "Optional inclusive lower bound on _time, ISO 8601 (e.g. 2026-04-12T07:00:00Z)." },
                        ["to"] = new { type = "string", description = "Optional exclusive upper bound on _time, ISO 8601." },
                        ["limit"] = new { type = "integer", description = "Maximum rows returned (default 100, max 1000)." },
                        ["traceId"] = new { type = "string", description = "Explicit W3C trace id override; otherwise the ambient OTel trace id is used." },
                    },
                    required = new[] { "query" },
                },
            },
            new
            {
                name = "observability.metrics.query",
                description = "Run a PromQL query against VictoriaMetrics. With neither from nor to the call is an instant query at the server's now. With one of from/to the call is an instant at that ISO 8601 timestamp. With both from and to the call is a range query (step is taken from the platform's configured scrape interval).",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["promql"] = new { type = "string", description = "PromQL expression." },
                        ["from"] = new { type = "string", description = "Lower bound on the time window, ISO 8601. Omit for an instant query at to (or at the server's now if to is also absent)." },
                        ["to"] = new { type = "string", description = "Upper bound on the time window, ISO 8601. Omit for an instant query at from (or at the server's now if from is also absent)." },
                    },
                    required = new[] { "promql" },
                },
            },
            new
            {
                name = "observability.logs.context",
                description = "Fetch the log rows whose trace_id matches a given W3C trace id. A faster path than the full search when the user already has a trace id (e.g. from a span error). The optional from/to/limit window narrows the trace-scoped query to a time range.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["traceId"] = new { type = "string", description = "W3C trace id (32-hex or 16-hex). Required." },
                        ["from"] = new { type = "string", description = "Optional inclusive lower bound on _time, ISO 8601." },
                        ["to"] = new { type = "string", description = "Optional exclusive upper bound on _time, ISO 8601." },
                        ["limit"] = new { type = "integer", description = "Maximum rows returned (default 100, max 1000)." },
                    },
                    required = new[] { "traceId" },
                },
            },
            new
            {
                name = "observability.metrics.series",
                description = "List the matching metric series for a label selector. Use to discover what label combinations exist for a metric before constructing a query.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["labelSelector"] = new { type = "string", description = "Label selector in Prometheus form, e.g. job=\"comuki-orchestrator\" or __name__=\"comuki.runs.queued\" (the { } wrapping is added on the wire)." },
                    },
                    required = new[] { "labelSelector" },
                },
            },
        };

        return JsonRpcResponse.Success(id, new { tools });
    }
}
