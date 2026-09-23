namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// Thrown when an inbound request can't be resolved against the
/// fakeScript — no entry left (script exhausted) or the entry the
/// ordering picked declares a <see cref="FakeScriptMatch"/> the request
/// doesn't satisfy. The endpoint turns this into a 500 Anthropic-shaped
/// error body rather than crashing the server or silently serving the
/// wrong turn.
/// </summary>
public sealed class FakeScriptException(string message) : Exception(message);
