namespace Comuki.Platform.Api.Contracts;

/// <summary>
/// Platform metadata returned by <c>GET /api/v1/info</c>. Stable shape — Kubb
/// generates a typed client from the OpenAPI schema that references this record.
/// New fields land in Phase 3+, not before.
/// </summary>
public sealed record InfoResponse(string Name, string Version, string Phase);
