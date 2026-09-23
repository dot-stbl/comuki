using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Verify.Application.Options;

/// <summary>
/// Background service tunables for the generic-command verifier worker.
/// Bound from <c>Verify:Verifier</c> in <c>HostComposer</c>; missing or
/// invalid values fail the boot via
/// <c>ValidateDataAnnotations + ValidateOnStart</c>.
/// </summary>
/// <remarks>
/// <b>Safety.</b> The runner executes <c>Executable</c> directly inside
/// the orchestrator host process (see
/// <c>Comuki.Modules.Verify.Infrastructure.Sync.GenericCommandProcessRunner</c>)
/// — there is no container/sandbox boundary yet; that is GH issue #47
/// (deferred to v2) and the same class of concern
/// <c>openspec/changes/harden-pi-worker-sandbox</c> addresses for worker
/// containers. <see cref="Enabled"/> therefore defaults to
/// <see langword="false"/>: the module ships wired but inert until an
/// operator explicitly opts a trusted environment in.
/// </remarks>
public sealed class VerifyOptions
{
    /// <summary>Configuration section the host binds from.</summary>
    public const string SectionName = "Verify:Verifier";

    /// <summary>
    /// Master switch — <see langword="false"/> (the default) keeps the
    /// worker registered but inert: every poll cycle is a no-op. See the
    /// isolation warning on this type for why the default is off.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Polling interval. The worker polls the due-run query on this
    /// cadence; 30s matches the rest of the platform's poll-based
    /// workers (Scheduler, Artifacts packager).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Per-cycle batch cap. The worker never claims more than this in
    /// one transaction so a single replica cannot starve other
    /// verifier pools.
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 10;

    /// <summary>
    /// Hard timeout for a single <c>Process.Start</c>. The runner kills
    /// the child after this elapses and stamps Red with the timeout
    /// reason.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan RunTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Soft cap on captured stdout+stderr (UTF-8 chars). Beyond this
    /// the runner keeps the tail of the stream and drops the head —
    /// truncation is logged but does not flip the verdict.
    /// </summary>
    [Range(1024, 1_048_576)]
    public int OutputLogCharCap { get; init; } = 65_536;
}
