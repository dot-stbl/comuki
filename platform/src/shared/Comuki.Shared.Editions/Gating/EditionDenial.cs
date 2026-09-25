using Microsoft.AspNetCore.Mvc;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// One deny decision: the status plus the ready ProblemDetails body.
/// Emitted by <see cref="EditionGate"/> for both feature and limit
/// denials (the same 403 + RFC 9457 shape applies to every gate-deny
/// branch the catalog recognises).
/// </summary>
/// <param name="StatusCode">HTTP status — 403 is the only status the
/// gate produces.</param>
/// <param name="Problem">ProblemDetails body to serialize.</param>
internal sealed record EditionDenial(int StatusCode, ProblemDetails Problem);
