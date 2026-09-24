using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.OpenAi.Errors;
using Comuki.TestFakeModel.OpenAi.Response;
using Comuki.TestFakeModel.Scripting;
using Comuki.TestFakeModel.Scripting.Model;

namespace Comuki.TestFakeModel.OpenAi;

/// <summary>
/// Maps <c>POST /v1/chat/completions</c> — the OpenAI-shape sibling of
/// <c>Anthropic.AnthropicMessagesEndpoint</c>, sharing the same
/// <see cref="FakeModelState"/> instance (registered once, injected into
/// both handlers): a single fakeScript's entries are consumed in one
/// sequence regardless of which wire shape the caller used to ask for the
/// next turn — design.md's "no code path knows it isn't talking to a real
/// provider" applies to the fake itself serving either protocol from one
/// script. No authentication middleware here either — see
/// <c>AnthropicMessagesEndpoint</c>'s doc.
/// </summary>
public static class OpenAiChatCompletionsEndpoint
{
    /// <summary>The OpenAI Chat Completions route — see <c>Anthropic.AnthropicMessagesEndpoint.RoutePath</c> for why this is a public constant.</summary>
    public const string RoutePath = "/v1/chat/completions";

    /// <summary>Registers the endpoint on <paramref name="endpoints"/>.</summary>
    public static IEndpointRouteBuilder MapOpenAiChatCompletions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(RoutePath, HandleAsync);
        return endpoints;
    }

    // Minimal API endpoint handler — private static, referenced as a method group from
    // MapPost above. Exempt from class-layout-and-tooling.md §1a's private-method ban
    // (exemption #3): this is the one place the rule allows it.
    private static async Task HandleAsync(HttpContext context, FakeModelState state, CancellationToken cancellationToken)
    {
        using var bodyReader = new StreamReader(context.Request.Body);
        var rawBody = await bodyReader.ReadToEndAsync(cancellationToken);

        if (!OpenAiChatCompletionsRequestReader.TryParseBody(rawBody, out var document, out var parseError))
        {
            await OpenAiChatCompletionsResponseWriter.WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                OpenAiErrors.InvalidRequest($"malformed JSON body: {parseError}"),
                cancellationToken);
            return;
        }

        using var disposableDocument = document;
        var observed = OpenAiRequestParser.Parse(document.RootElement);
        var requestIndex = state.NextRequestIndex();

        state.Record(new RecordedRequest(
            requestIndex,
            context.Request.Path.Value ?? RoutePath,
            context.Request.Headers["x-api-key"].FirstOrDefault(),
            context.Request.Headers.Authorization.FirstOrDefault(),
            observed.Stream,
            observed.Model,
            rawBody,
            observed.LastUserMessageText,
            observed.HasToolResult,
            state.Clock.GetUtcNow()));

        FakeScriptEntry entry;
        try
        {
            entry = state.Resolve(requestIndex, observed);
        }
        catch (FakeScriptException ex)
        {
            await OpenAiChatCompletionsResponseWriter.WriteErrorAsync(context, StatusCodes.Status500InternalServerError, OpenAiErrors.ScriptFailure(ex.Message), cancellationToken);
            return;
        }

        var chatCompletionId = DeterministicIds.ChatCompletionId(state.ScenarioName, requestIndex);
        var createdUnixSeconds = state.Clock.GetUtcNow().ToUnixTimeSeconds();

        if (observed.Stream)
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await OpenAiSseWriter.WriteStreamAsync(context.Response, chatCompletionId, observed.Model, createdUnixSeconds, state.ScenarioName, requestIndex, entry.Response, cancellationToken);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(
            OpenAiResponseFactory.BuildNonStreaming(chatCompletionId, observed.Model, createdUnixSeconds, state.ScenarioName, requestIndex, entry.Response),
            JsonSerializerOptions.Web,
            cancellationToken);
    }
}

/// <summary>The request-body parsing step <see cref="OpenAiChatCompletionsEndpoint"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class OpenAiChatCompletionsRequestReader
{
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
}

/// <summary>The error-response-writing step <see cref="OpenAiChatCompletionsEndpoint"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class OpenAiChatCompletionsResponseWriter
{
    public static Task WriteErrorAsync(HttpContext context, int statusCode, OpenAiErrorBody body, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(body, JsonSerializerOptions.Web, cancellationToken);
    }
}
