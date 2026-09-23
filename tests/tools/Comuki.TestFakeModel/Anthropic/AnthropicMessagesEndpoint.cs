using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.Scripting;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>
/// Maps <c>POST /v1/messages</c> — the fake's only endpoint. No
/// authentication middleware is registered anywhere in
/// <see cref="FakeModelServer"/>, so any (or no) <c>x-api-key</c> /
/// <c>Authorization</c> header is accepted; both are recorded on
/// <see cref="RecordedRequest"/> for the caller to assert on if it cares.
/// </summary>
internal static class AnthropicMessagesEndpoint
{
    /// <summary>Registers the endpoint on <paramref name="endpoints"/>.</summary>
    public static IEndpointRouteBuilder MapAnthropicMessages(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/messages", HandleAsync);
        return endpoints;
    }

    private static async Task HandleAsync(HttpContext context, FakeModelState state, CancellationToken cancellationToken)
    {
        using var bodyReader = new StreamReader(context.Request.Body);
        var rawBody = await bodyReader.ReadToEndAsync(cancellationToken);

        if (!TryParseBody(rawBody, out var document, out var parseError))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                AnthropicErrors.InvalidRequest($"malformed JSON body: {parseError}"),
                cancellationToken);
            return;
        }

        using var disposableDocument = document;
        var observed = AnthropicRequestParser.Parse(document.RootElement);
        var requestIndex = state.NextRequestIndex();

        state.Record(new RecordedRequest(
            requestIndex,
            context.Request.Path.Value ?? "/v1/messages",
            context.Request.Headers["x-api-key"].FirstOrDefault(),
            context.Request.Headers.Authorization.FirstOrDefault(),
            observed.Stream,
            observed.Model,
            rawBody,
            observed.LastUserMessageText,
            observed.HasToolResult,
            state.Clock.UtcNow()));

        FakeScriptEntry entry;
        try
        {
            entry = state.Resolve(requestIndex, observed);
        }
        catch (FakeScriptException ex)
        {
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, AnthropicErrors.ScriptFailure(ex.Message), cancellationToken);
            return;
        }

        var messageId = DeterministicIds.MessageId(state.ScenarioName, requestIndex);

        if (observed.Stream)
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await new AnthropicSseWriter(context.Response)
                .WriteStreamAsync(messageId, observed.Model, state.ScenarioName, requestIndex, entry.Response, cancellationToken);
            return;
        }

        var response = AnthropicResponseFactory.BuildNonStreaming(messageId, observed.Model, state.ScenarioName, requestIndex, entry.Response);
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(response, AnthropicJsonOptions.Default, cancellationToken);
    }

    private static bool TryParseBody(string rawBody, [NotNullWhen(true)] out JsonDocument? document, out string error)
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

    private static Task WriteErrorAsync(HttpContext context, int statusCode, AnthropicErrorBody body, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(body, AnthropicJsonOptions.Default, cancellationToken);
    }
}
