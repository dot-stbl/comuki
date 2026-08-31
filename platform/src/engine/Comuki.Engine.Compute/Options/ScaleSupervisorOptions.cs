using System.ComponentModel.DataAnnotations;

namespace Comuki.Engine.Compute.Options;

/// <summary>
/// Scale supervisor settings. Projects and profile keys decide what the
/// supervisor polls; the numeric knobs are the DEFAULTS every project uses
/// until a per-project override lands in <c>IProjectScaleSettings</c>.
/// </summary>
public sealed class ScaleSupervisorOptions
{
    public const string SectionName = "Compute:Scale";

    /// <summary>Default worker image (digest-pinned in production).</summary>
    [Required]
    [MinLength(1)]
    public string WorkerImage { get; init; } = "ghcr.io/comuki/worker:latest";

    /// <summary>Default pinned git ref of the profiles repo stamped on started workers.</summary>
    [Required]
    [MinLength(1)]
    public string ProfilesGitRef { get; init; } = "main";

    /// <summary>Orchestrator gRPC endpoint the worker containers connect back to.</summary>
    [Required]
    public Uri OrchestratorGrpcUrl { get; init; } = new("http://localhost:5051");

    /// <summary>Delay between supervisor passes.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Default warm-idle floor per profile: idle workers are never reaped below it.</summary>
    [Range(0, 1000)]
    public int MinIdle { get; init; }

    /// <summary>Default cap on concurrently running workers per project.</summary>
    [Range(1, 1000)]
    public int MaxConcurrent { get; init; } = 4;

    /// <summary>Default idle TTL: an idle worker past it is a reaper candidate.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "24:00:00")]
    public TimeSpan IdleTtl { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Project ids the supervisor manages. Empty list — the supervisor pass is a no-op.</summary>
    public IReadOnlyList<Guid> Projects { get; init; } = [];

    /// <summary>Profile keys polled per project (backlog is read per profile).</summary>
    public IReadOnlyList<string> ProfileKeys { get; init; } = [];
}
