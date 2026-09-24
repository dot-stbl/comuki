using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Comuki.TestFakeModel.Anthropic.Errors;
using Comuki.TestFakeModel.OpenAi;
using Comuki.TestFakeModel.OpenAi.Errors;

namespace Comuki.TestFakeModel.Cassettes.Endpoints;

/// <summary>
/// Request-parsing and error-response steps shared by
/// <see cref="CassetteReplayEndpoint"/> and <see cref="CassetteRecordingEndpoint"/>
/// — a named <c>internal static class</c> rather than two <c>file</c>-scoped
/// copies, since both endpoint files need the exact same behavior
/// (class-layout-and-tooling.md §1a: "helper logic that crosses feature
/// boundaries or is reused" → dedicated file). Error bodies are shaped
/// per the request's own protocol (Anthropic vs. OpenAI, by route path) —
/// see <c>Anthropic.AnthropicMessagesEndpoint</c>'s equivalent, non-shared
/// version for fake mode.
/// </summary>
internal static class CassetteEndpointIO
{
    /// <summary>
    /// The catch-all route both <see cref="CassetteReplayEndpoint"/> and
    /// <see cref="CassetteRecordingEndpoint"/> map — one source per
    /// api-route-constants.md §1, since the literal would otherwise be
    /// hand-typed identically in both files.
    /// </summary>
    public const string CatchAllRoutePath = "/{**catchAll}";

    /// <summary>Parses <paramref name="rawBody"/> as JSON.</summary>
    public static bool TryParseBody(string rawBody, [NotNullWhen(true)] out JsonDocument? document, out string error)
    {
        try
        {
            document = JsonDocument.Parse(rawBody);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            document = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Writes a 400 invalid-request error shaped for <paramref name="path"/>'s protocol.</summary>
    public static Task WriteInvalidRequestAsync(HttpContext context, string path, string message, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        object body = path == OpenAiChatCompletionsEndpoint.RoutePath ? OpenAiErrors.InvalidRequest(message) : AnthropicErrors.InvalidRequest(message);
        return context.Response.WriteAsJsonAsync(body, JsonSerializerOptions.Web, cancellationToken);
    }

    /// <summary>Writes a 500 script/cassette failure shaped for <paramref name="path"/>'s protocol.</summary>
    public static Task WriteFailureAsync(HttpContext context, string path, string message, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        object body = path == OpenAiChatCompletionsEndpoint.RoutePath ? OpenAiErrors.ScriptFailure(message) : AnthropicErrors.ScriptFailure(message);
        return context.Response.WriteAsJsonAsync(body, JsonSerializerOptions.Web, cancellationToken);
    }
}
