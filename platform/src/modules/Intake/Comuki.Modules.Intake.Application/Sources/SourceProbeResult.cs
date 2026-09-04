namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>
/// The outcome of a probe — does the upstream answer, how fast, what id
/// would the host suggest for a follow-up create. The <c>latencyMs</c>
/// is measured end-to-end (HTTP request to first response byte) and is
/// only meaningful when <c>reachable</c> is true.
/// </summary>
/// <param name="Reachable">Upstream answered (any HTTP status counts as reachable).</param>
/// <param name="LatencyMs">Round-trip latency when reachable; 0 otherwise.</param>
/// <param name="SuggestedId">Optional id the provider returned (e.g. GitHub repo id) — null when unknown.</param>
/// <param name="Message">Provider-specific short status sentence (e.g. <c>"github: 200 OK"</c>) for the operator.</param>
public sealed record SourceProbeResult(
    bool Reachable,
    long LatencyMs,
    string? SuggestedId,
    string Message);
