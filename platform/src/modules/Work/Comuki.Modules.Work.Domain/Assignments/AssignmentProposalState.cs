namespace Comuki.Modules.Work.Domain.Assignments;

/// <summary>
/// Closed set of assignment states — see <see cref="WorkTaskAssignment"/>.
/// The umbrella's task 7.5 invariant: a human assignment is a proposal
/// that requires a distinct-human approval before it becomes effective;
/// a self-assignment without approval is rejected and the proposal is
/// logged for audit.
/// </summary>
public enum AssignmentProposalState
{
    /// <summary>Direct auto-assigned — effective immediately (Service under policy).</summary>
    Direct = 1,

    /// <summary>A proposal raised by an actor; awaiting a distinct-human approval.</summary>
    Proposed = 2,

    /// <summary>Approved by a distinct human — the assignment is now effective.</summary>
    Approved = 3,

    /// <summary>Rejected by a distinct human — the assignment is not effective; the proposal stays in the audit log.</summary>
    Rejected = 4,
}
