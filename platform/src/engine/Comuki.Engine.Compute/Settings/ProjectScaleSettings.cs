namespace Comuki.Engine.Compute.Settings;

/// <summary>
/// Per-project scale knobs. Null image/ref/env-class fall back to the
/// supervisor options defaults; the supervisor stamps both onto every
/// started worker (labels carry class/digest/ref for claim matching).
/// </summary>
/// <param name="MinIdle">Warm-idle floor per profile.</param>
/// <param name="MaxConcurrent">Cap on concurrently running workers per project.</param>
/// <param name="IdleTtl">Idle workers past this TTL become reaper candidates.</param>
/// <param name="WorkerImage">Optional image override (digest-pinned).</param>
/// <param name="ProfilesGitRef">Optional profiles git ref override.</param>
/// <param name="EnvClass">
///     Environment-class id the project binds to (catalog id, e.g.
///     <c>"net10-sdk-bun"</c>). Null means "engine default" — the
///     supervisor falls back to <see cref="Options.ScaleSupervisorOptions.DefaultEnvClass"/>.
///     Workers of a different class do not satisfy this project's backlog
///     (worker-environments spec §"Scale policy per (profile, env class)").
/// </param>
public sealed record ProjectScaleSettings(
    int MinIdle,
    int MaxConcurrent,
    TimeSpan IdleTtl,
    string? WorkerImage = null,
    string? ProfilesGitRef = null,
    string? EnvClass = null);
