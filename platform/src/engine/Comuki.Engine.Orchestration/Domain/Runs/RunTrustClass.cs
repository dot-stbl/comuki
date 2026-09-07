namespace Comuki.Engine.Orchestration.Domain.Runs;

/// <summary>
/// Trust class of a run — the platform's autonomy ratchet. A run starts at
/// <see cref="Supervised"/> (every action reviewed, the default state); the
/// ratchet promotes it to <see cref="Pilot"/> after the operator opts in via
/// a setting on the worker profile, and then to <see cref="Trusted"/> after
/// <c>N</c> consecutive <see cref="RunStatus.Succeeded"/> runs (the threshold
/// is configured in <c>TrustClassRatchetOptions</c>). A failure while
/// <see cref="Trusted"/> demotes the run back to <see cref="Supervised"/>.
/// Mutations are guarded by <see cref="Run.PromoteTo"/> /
/// <see cref="Run.DemoteTo"/>.
/// </summary>
public enum RunTrustClass
{
    /// <summary>Default — every action needs a human in the loop.</summary>
    Supervised = 0,

    /// <summary>Opted-in <see cref="Supervised"/>; ratchet watches completions.</summary>
    Pilot = 1,

    /// <summary>Auto-promoted after enough consecutive successes; failures demote back.</summary>
    Trusted = 2,
}
