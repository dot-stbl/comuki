using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Comuki.TestFakeModel.Anthropic.Errors;
using Comuki.TestFakeModel.Anthropic.Response;
using Comuki.TestFakeModel.Determinism;
using Comuki.TestFakeModel.Scripting;
using Comuki.TestFakeModel.Scripting.Model;

namespace Comuki.TestFakeModel.Anthropic;

/// <summary>
/// Maps <c>POST /v1/messages</c> — the fake's only endpoint. No
/// authentication middleware is registered anywhere in
/// <c>FakeModelServer</c>, so any (or no) <c>x-api-key</c> /
/// <c>Authorization</c> header is accepted; both are recorded on
/// <c>RecordedRequest</c> for the caller to assert on if it cares.
/// </summary>
public static class AnthropicMessagesEndpoint
{
    /// <summary>Registers the endpoint on <paramref name="endpoints"/>.</summary>
    public static IEndpointRouteBuilder MapAnthropicMessages(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/messages", HandleAsync);
        return endpoints;
    }

    // Minimal API endpoint handler — private static, referenced as a method group from
    // MapPost above. Exempt from class-layout-and-tooling.md §1a's private-method ban
    // (exemption #3): this is the one place the rule allows it.
    private static async Task HandleAsync(HttpContext context, FakeModelState state, CancellationToken cancellationToken)
    {
        using var bodyReader = new StreamReader(context.Request.Body);
        var rawBody = await bodyReader.ReadToEndAsync(cancellationToken);

        if (!AnthropicMessagesRequestReader.TryParseBody(rawBody, out var document, out var parseError))
        {
            await AnthropicMessagesResponseWriter.WriteErrorAsync(
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
            state.Clock.GetUtcNow()));

        FakeScriptEntry entry;
        try
        {
            entry = state.Resolve(requestIndex, observed);
        }
        catch (FakeScriptException ex)
        {
            await AnthropicMessagesResponseWriter.WriteErrorAsync(context, StatusCodes.Status500InternalServerError, AnthropicErrors.ScriptFailure(ex.Message), cancellationToken);
            return;
        }

        var messageId = DeterministicIds.MessageId(state.ScenarioName, requestIndex);

        if (observed.Stream)
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await AnthropicSseWriter.WriteStreamAsync(context.Response, messageId, observed.Model, state.ScenarioName, requestIndex, entry.Response, cancellationToken);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(
            AnthropicResponseFactory.BuildNonStreaming(messageId, observed.Model, state.ScenarioName, requestIndex, entry.Response),
            JsonSerializerOptions.Web,
            cancellationToken);
    }
}

/// <summary>The request-body parsing step <see cref="AnthropicMessagesEndpoint"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class AnthropicMessagesRequestReader
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

/// <summary>The error-response-writing step <see cref="AnthropicMessagesEndpoint"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class AnthropicMessagesResponseWriter
{
    public static Task WriteErrorAsync(HttpContext context, int statusCode, AnthropicErrorBody body, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(body, JsonSerializerOptions.Web, cancellationToken);
    }
}
