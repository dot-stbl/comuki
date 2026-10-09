namespace Comuki.Modules.Work.Domain.Completion;

/// <summary>
/// Closed set of evidence kinds a <see cref="WorkTaskCompletionPolicy"/>
/// requires on <c>Work.Resolve</c>. The aggregate guard rejects a Resolve that
/// does not carry every required kind (the umbrella's task 8.2 invariant;
/// surfaces as <c>422 work.completion.evidence-incomplete</c>).
/// </summary>
public enum EvidenceKind
{
    /// <summary>The RunReport from the active attempt (the engine's run-events journal).</summary>
    RunReport = 1,

    /// <summary>An artifact pointer the attempt produced (stored in the Artifacts module).</summary>
    ArtifactPointer = 2,

    /// <summary>The VerificationRunReport from a follow-up verification run.</summary>
    VerificationRunReport = 3,

    /// <summary>A human attestation (signed by the actor who proposed the outcome).</summary>
    HumanAttestation = 4,
}
