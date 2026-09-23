namespace Comuki.TestFakeModel.Anthropic;

/// <summary>
/// The parts of an inbound <c>POST /v1/messages</c> request the fakeScript
/// matcher and the recorded-request log care about. Deliberately not a
/// full typed mirror of the Anthropic Messages request — <c>messages[]</c>
/// content is a polymorphic union (plain string or a block array mixing
/// text/tool_use/tool_result/image/document), so <c>AnthropicRequestParser</c>
/// reads it with <see cref="System.Text.Json.JsonDocument"/> the same way
/// <c>Comuki.Modules.Proxy.Application.Extraction.AnthropicUsageExtractor</c>
/// reads the response shape.
/// </summary>
public sealed record ObservedRequest(string? LastUserMessageText, bool HasToolResult, string Model, bool Stream);
