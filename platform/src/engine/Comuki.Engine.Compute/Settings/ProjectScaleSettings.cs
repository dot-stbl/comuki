namespace Comuki.Engine.Compute.Settings;

/// <summary>
/// Per-project scale knobs. Null image/ref fall back to the supervisor
/// options defaults; the supervisor stamps both onto every started worker
/// (labels carry digest/ref for claim matching).
/// </summary>
/// <param name="MinIdle">Warm-idle floor per profile.</param>
/// <param name="MaxConcurrent">Cap on concurrently running workers per project.</param>
/// <param name="IdleTtl">Idle workers past this TTL become reaper candidates.</param>
/// <param name="WorkerImage">Optional image override (digest-pinned).</param>
/// <param name="ProfilesGitRef">Optional profiles git ref override.</param>
public sealed record ProjectScaleSettings(
    int MinIdle,
    int MaxConcurrent,
    TimeSpan IdleTtl,
    string? WorkerImage = null,
    string? ProfilesGitRef = null);
