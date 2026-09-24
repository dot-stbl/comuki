namespace Comuki.TestFakeModel;

/// <summary>
/// The parts of an inbound scripted-model request the fakeScript matcher
/// and the recorded-request log care about — protocol-agnostic: both
/// <c>Anthropic.AnthropicRequestParser</c> (<c>POST /v1/messages</c>) and
/// <c>OpenAi.OpenAiRequestParser</c> (<c>POST /v1/chat/completions</c>)
/// produce this same shape, so one fakeScript and one
/// <c>Scripting.FakeScriptEntryResolver</c> serve either wire shape
/// unchanged (design.md's "no code path knows it isn't talking to a real
/// provider"). Deliberately not a full typed mirror of either request —
/// each protocol's <c>messages[]</c> content is a polymorphic union (plain
/// string or a block/part array), so each parser reads it with
/// <see cref="System.Text.Json.JsonDocument"/> the same way
/// <c>Comuki.Modules.Proxy.Application.Extraction.AnthropicUsageExtractor</c>
/// /
/// <c>Comuki.Modules.Proxy.Application.Extraction.OpenAiUsageExtractor</c>
/// read the response shape. Lives at the project root (no sub-namespace),
/// same reasoning as <see cref="RecordedRequest"/>: constructed by two
/// sibling protocol namespaces and consumed by <c>Scripting</c> and
/// <c>Cassettes</c> — a shared, dependency-free data shape none of those
/// layers should have to depend on each other's protocol namespace to
/// reach.
/// </summary>
public sealed record ObservedRequest(string? LastUserMessageText, bool HasToolResult, string Model, bool Stream);
