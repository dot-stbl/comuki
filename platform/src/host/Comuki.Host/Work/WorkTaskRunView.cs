using System.Text.Json.Serialization;

namespace Comuki.Host.Work;

/// <summary>
/// Run-shaped compatibility projection of a WorkTask's current attempt —
/// the umbrella's <c>add-work-management</c> design §"Run-consumer
/// compatibility projection". Shape-compatible with the pre-<c>WorkTask</c>
/// <c>GET /api/v1/runs/{runId}</c> response and includes the
/// authoritative <c>taskId</c> field. 404 when the requested attempt
/// has been fenced by a replacement (per the design's
/// "Superseded attempt returns 404" scenario).
/// </summary>
public sealed partial record WorkTaskRunView(
    [property: JsonPropertyName("taskId")] Guid TaskId,
    [property: JsonPropertyName("attemptOrdinal")] int AttemptOrdinal,
    [property: JsonPropertyName("runId")] Guid RunId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);
