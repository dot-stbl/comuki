using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comuki.Host.Mcp;

/// <summary>
/// JSON-RPC 2.0 envelope — request / response / error shapes the
/// <c>POST /api/v1/mcp</c> endpoint speaks. The wire key is <c>jsonrpc</c>
/// (the spec mandates the lowercase field name); <c>id</c> is a string or
/// integer and the response echoes whichever the caller used (notification
/// requests carry no <c>id</c> and produce no response).
/// </summary>
public static class JsonRpcEnvelope
{
    /// <summary>JSON-RPC version — every payload carries it.</summary>
    public const string Version = "2.0";

    /// <summary>Standard JSON-RPC error codes we surface to callers.</summary>
    public static class ErrorCodes
    {
        /// <summary>Invalid JSON was received.</summary>
        public const int ParseError = -32700;

        /// <summary>JSON was received but the envelope shape is wrong.</summary>
        public const int InvalidRequest = -32600;

        /// <summary>The method does not exist or is unavailable.</summary>
        public const int MethodNotFound = -32601;

        /// <summary>Method parameters are invalid.</summary>
        public const int InvalidParams = -32602;

        /// <summary>Internal JSON-RPC error.</summary>
        public const int InternalError = -32603;

        /// <summary>Server error — upper bound of -32000 to -32099 is reserved for the server.</summary>
        public const int ServerError = -32000;
    }
}

/// <summary>
/// JSON-RPC 2.0 request envelope — <c>method</c> is the JSON-RPC method
/// name (for tools: <c>tools/call</c>), <c>params</c> is the typed
/// argument object the caller wants the host to act on.
/// </summary>
public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

/// <summary>
/// JSON-RPC 2.0 success response — <c>result</c> is the typed payload
/// the tool produced.
/// </summary>
public sealed record JsonRpcSuccess(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("result")] JsonElement Result);

/// <summary>
/// JSON-RPC 2.0 error response — <c>code</c> + <c>message</c> are the
/// standard JSON-RPC error shape; <c>data</c> is optional structured
/// detail (we use it for the host's stable error codes).
/// </summary>
public sealed record JsonRpcError(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("error")] JsonRpcErrorBody Error);

/// <summary>The error body carried inside a <see cref="JsonRpcError"/>.</summary>
/// <param name="Code">JSON-RPC standard or host-defined error code.</param>
/// <param name="Message">Human-readable, single-line summary.</param>
/// <param name="Data">Optional structured detail (carries the stable code when one exists).</param>
public sealed record JsonRpcErrorBody(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] object? Data);

/// <summary>
/// One tool call inside a JSON-RPC <c>tools/call</c> envelope —
/// <c>name</c> is the tool key (e.g. <c>knowledge.search</c>),
/// <c>arguments</c> are the tool's named parameters.
/// </summary>
public sealed record ToolCallParams(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonElement? Arguments);

/// <summary>
/// One tool result the dispatcher returns to the caller — <c>content</c>
/// is an array of typed blocks; <c>isError</c> flips when the tool ran
/// but produced no result (vs. raised an exception that mapped to a
/// JSON-RPC error).
/// </summary>
/// <param name="Content">The blocks the tool produced (text today; structured types in a later slice).</param>
/// <param name="IsError">True when the tool ran but reported failure (vs. raising a JSON-RPC error).</param>
public sealed record ToolResult(
    [property: JsonPropertyName("content")] IReadOnlyList<ToolContentBlock> Content,
    [property: JsonPropertyName("isError")] bool IsError);

/// <summary>One block of tool output — the <c>text</c> flavour carries JSON or human text.</summary>
/// <param name="Type">Always <c>text</c> today — future slices add <c>image</c> / <c>resource</c>.</param>
/// <param name="Text">The rendered text — JSON-encoded for structured results.</param>
public sealed record ToolContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);
