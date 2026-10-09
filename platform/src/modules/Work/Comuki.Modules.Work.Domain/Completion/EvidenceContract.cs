namespace Comuki.Modules.Work.Domain.Completion;

/// <summary>
/// Closed set of evidence kinds a <see cref="WorkTaskCompletionPolicy"/>
/// requires — the umbrella's task 8.1 contract. A new Task without a policy
/// is rejected at creation (<c>422 work.completion.policy-missing</c>); a
/// Resolve without every required kind is rejected
/// (<c>422 work.completion.evidence-incomplete</c>).
/// </summary>
public sealed record EvidenceContract(IReadOnlySet<EvidenceKind> RequiredKinds)
{
    /// <summary>Single-kind contract — only the RunReport is required.</summary>
    public static readonly EvidenceContract RunReportOnly = new(new HashSet<EvidenceKind>([EvidenceKind.RunReport]));

    /// <summary>Default contract — RunReport + VerificationRunReport (the post-verification flow).</summary>
    public static readonly EvidenceContract Default = new(new HashSet<EvidenceKind>(
        [EvidenceKind.RunReport, EvidenceKind.VerificationRunReport]));

    /// <summary>HumanReviewer contract — RunReport + VerificationRunReport + HumanAttestation.</summary>
    public static readonly EvidenceContract HumanReviewer = new(new HashSet<EvidenceKind>(
        [EvidenceKind.RunReport, EvidenceKind.VerificationRunReport, EvidenceKind.HumanAttestation]));

    /// <summary>True when <paramref name="actual"/> covers every required kind.</summary>
    public bool IsSatisfiedBy(IReadOnlySet<EvidenceKind> actual)
    {
        return RequiredKinds.All(actual.Contains);
    }

    /// <summary>True when the contract is non-empty — a Task without a policy is rejected at creation.</summary>
    public bool IsWellFormed => RequiredKinds.Count > 0;
}
