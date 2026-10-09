using System.Text.Json.Serialization;

namespace Comuki.Host.Work;

/// <summary>
/// Per-Task detail projection — the
/// <c>GET /api/v1/work/tasks/{taskId}</c> response shape
/// (the umbrella's <c>add-work-management</c> design §"Work
/// API"). Caller-facing fields only — no aggregate internals.
/// </summary>
public sealed partial record WorkTaskDetailView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("brief")] string Brief,
    [property: JsonPropertyName("briefVersion")] int BriefVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("resolutionOutcome")] string? ResolutionOutcome,
    [property: JsonPropertyName("attemptOrdinal")] int AttemptOrdinal,
    [property: JsonPropertyName("activeAttemptId")] Guid? ActiveAttemptId,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("missionId")] Guid? MissionId,
    [property: JsonPropertyName("sourceRefs")] IReadOnlyList<WorkTaskSourceRefView> SourceRefs,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);
