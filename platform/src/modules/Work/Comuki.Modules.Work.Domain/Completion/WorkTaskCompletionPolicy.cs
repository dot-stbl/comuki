namespace Comuki.Modules.Work.Domain.Completion;

/// <summary>
/// One versioned completion policy attached to a WorkTask. The policy
/// carries the kind + a typed <see cref="EvidenceContract"/>; a policy
/// change creates a new <see cref="Version"/> (no silent mutation of
/// historical evidence requirements — the umbrella's task 8.5 invariant).
/// The Resolve command reads the policy version that was active at
/// the attempt's terminal time, not the current one.
/// </summary>
public sealed record WorkTaskCompletionPolicy(CompletionPolicyKind Kind, EvidenceContract Contract, int Version)
{
    /// <summary>Default factory — version 1, Deterministic kind, <see cref="EvidenceContract.RunReportOnly"/>.</summary>
    public static WorkTaskCompletionPolicy Default(DateTimeOffset now)
    {
        return new(CompletionPolicyKind.Deterministic, EvidenceContract.RunReportOnly, Version: 1);
    }

    /// <summary>
    /// Returns a new policy with the same kind + contract and version + 1.
    /// A policy change is auditable — the resolver keeps the prior version
    /// alongside the new one and the audit row carries both ids.
    /// </summary>
    public WorkTaskCompletionPolicy WithUpdatedContract(EvidenceContract newContract)
    {
        return this with { Contract = newContract, Version = Version + 1 };
    }
}
