using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Table-driven legal-transition coverage for <see cref="MergeQueueTransitions"/>.
/// Mirrors <c>WorkItemTransitionsShould</c> in shape but covers the
/// merge-queue status set.
/// </summary>
public sealed class MergeQueueTransitionsShould
{
    [Theory(DisplayName = "Given two statuses, when IsLegal is called, then it matches the transition table")]
    [MemberData(nameof(LegalTransitionMatrix))]
    public void CheckLegalTransitions(MergeQueueStatus from, MergeQueueStatus to, bool legal)
    {
        MergeQueueTransitions.IsLegal(from, to).ShouldBe(legal);
    }

    /// <summary>
    /// Full <c>(from, to, legal)</c> matrix — <see cref="MemberDataAttribute"/>
    /// because <see cref="InlineDataAttribute"/> requires constants and
    /// <see cref="MergeQueueStatus"/> members are static properties.
    /// </summary>
    public static TheoryData<MergeQueueStatus, MergeQueueStatus, bool> LegalTransitionMatrix
    {
        get
        {
            var data = new TheoryData<MergeQueueStatus, MergeQueueStatus, bool>
            {
                { MergeQueueStatus.Pending, MergeQueueStatus.InProgress, true },
                { MergeQueueStatus.Pending, MergeQueueStatus.Abandoned, true },
                { MergeQueueStatus.Pending, MergeQueueStatus.Merged, false },
                { MergeQueueStatus.InProgress, MergeQueueStatus.Merged, true },
                { MergeQueueStatus.InProgress, MergeQueueStatus.Abandoned, true },
                { MergeQueueStatus.InProgress, MergeQueueStatus.Pending, true },
                { MergeQueueStatus.Merged, MergeQueueStatus.Pending, false },
                { MergeQueueStatus.Merged, MergeQueueStatus.InProgress, false },
                { MergeQueueStatus.Merged, MergeQueueStatus.Abandoned, false },
                { MergeQueueStatus.Abandoned, MergeQueueStatus.Pending, false },
                { MergeQueueStatus.Abandoned, MergeQueueStatus.InProgress, false },
                { MergeQueueStatus.Abandoned, MergeQueueStatus.Merged, false },
            };
            return data;
        }
    }

    [Fact(DisplayName = "Given Merged status, when TargetsFrom is called, then it is empty (terminal)")]
    public void MergedIsTerminal()
    {
        MergeQueueTransitions.TargetsFrom(MergeQueueStatus.Merged).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given Abandoned status, when TargetsFrom is called, then it is empty (terminal)")]
    public void AbandonedIsTerminal()
    {
        MergeQueueTransitions.TargetsFrom(MergeQueueStatus.Abandoned).ShouldBeEmpty();
    }
}
