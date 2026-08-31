using System.ComponentModel.DataAnnotations;

namespace Comuki.Host.Translator;

/// <summary>
/// Everything the worker container needs to run: where the orchestrator
/// lives (REST + gRPC), the worker token (from <c>COMUKI_WORKER_TOKEN</c>),
/// the claim labels (image / profiles ref / profile key from the
/// <c>COMUKI_*</c> environment the compute provider stamped), and the pi
/// executable to spawn. Bound from the <c>Translator</c> config section;
/// <see cref="Program"/> maps the <c>COMUKI_*</c> env onto it.
/// </summary>
public sealed class TranslatorOptions
{
    /// <summary>Config section: <c>Translator</c>.</summary>
    public const string SectionName = "Translator";

    /// <summary>Base URL of the orchestrator REST API (claim/heartbeat/complete/fail).</summary>
    [Required]
    public required Uri OrchestratorBaseUrl { get; init; }

    /// <summary>URL of the orchestrator gRPC endpoint (worker bidi stream).</summary>
    [Required]
    public required Uri OrchestratorGrpcUrl { get; init; }

    /// <summary>Opaque worker token; validated by the orchestrator on every call.</summary>
    [Required]
    [MinLength(16)]
    public required string WorkerToken { get; init; }

    /// <summary>Profile key this worker was scaled for (claim label).</summary>
    [Required]
    public required string ProfileKey { get; init; }

    /// <summary>Pinned profiles git ref this worker runs (claim label).</summary>
    [Required]
    public required string ProfilesRef { get; init; }

    /// <summary>Worker image digest (claim label — mirrors the container image).</summary>
    [Required]
    public required string WorkerImage { get; init; }

    /// <summary>Executable spawned per work item. Production: <c>pi</c>; tests: TestFakePi.</summary>
    public string PiExecutable { get; init; } = "pi";

    /// <summary>Working directory for the spawned process (the mounted worktree).</summary>
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();

    /// <summary>Local mounted directory with client profiles; unset skips the copy with a warning.</summary>
    public string? ProfilesPath { get; init; }

    /// <summary>Public git URL with client profiles; used when <see cref="ProfilesPath"/> is unset.</summary>
    public Uri? ProfilesGitUrl { get; init; }

    /// <summary>How long to wait between claim attempts when the queue has nothing.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan ClaimPollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How often to extend the lease while an item is running.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);
}
