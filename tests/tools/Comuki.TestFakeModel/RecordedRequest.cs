namespace Comuki.TestFakeModel;

/// <summary>
/// One <c>POST /v1/messages</c> request the fake observed — recorded for
/// test assertions (e.g. "the proxy stamp reached the request" or "pi sent
/// exactly two turns"). <see cref="ApiKeyHeader"/>/<see cref="AuthorizationHeader"/>
/// are recorded, never required — the fake accepts any credential (or
/// none) and leaves asserting on them to the caller.
/// </summary>
public sealed record RecordedRequest(
    int Index,
    string Path,
    string? ApiKeyHeader,
    string? AuthorizationHeader,
    bool Stream,
    string Model,
    string RawBody,
    string? LastUserMessageText,
    bool HasToolResult,
    DateTimeOffset ReceivedAt);
