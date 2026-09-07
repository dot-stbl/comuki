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
    [InlineData(MergeQueueStatus.Pending, MergeQueueStatus.InProgress, true)]
    [InlineData(MergeQueueStatus.Pending, MergeQueueStatus.Abandoned, true)]
    [InlineData(MergeQueueStatus.Pending, MergeQueueStatus.Merged, false)]
    [InlineData(MergeQueueStatus.InProgress, MergeQueueStatus.Merged, true)]
    [InlineData(MergeQueueStatus.InProgress, MergeQueueStatus.Abandoned, true)]
    [InlineData(MergeQueueStatus.InProgress, MergeQueueStatus.Pending, true)]
    [InlineData(MergeQueueStatus.Merged, MergeQueueStatus.Pending, false)]
    [InlineData(MergeQueueStatus.Merged, MergeQueueStatus.InProgress, false)]
    [InlineData(MergeQueueStatus.Merged, MergeQueueStatus.Abandoned, false)]
    [InlineData(MergeQueueStatus.Abandoned, MergeQueueStatus.Pending, false)]
    [InlineData(MergeQueueStatus.Abandoned, MergeQueueStatus.InProgress, false)]
    [InlineData(MergeQueueStatus.Abandoned, MergeQueueStatus.Merged, false)]
    public void CheckLegalTransitions(MergeQueueStatus from, MergeQueueStatus to, bool legal)
    {
        MergeQueueTransitions.IsLegal(from, to).ShouldBe(legal);
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
