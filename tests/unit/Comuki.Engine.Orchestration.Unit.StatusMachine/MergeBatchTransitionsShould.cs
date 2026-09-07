using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Table-driven legal-transition coverage for
/// <see cref="MergeBatchTransitions"/>. Mirrors
/// <see cref="MergeQueueTransitionsShould"/> in shape.
/// </summary>
public sealed class MergeBatchTransitionsShould
{
    [Theory(DisplayName = "Given two statuses, when IsLegal is called, then it matches the transition table")]
    [InlineData(MergeBatchStatus.Pending, MergeBatchStatus.InProgress, true)]
    [InlineData(MergeBatchStatus.Pending, MergeBatchStatus.Merged, false)]
    [InlineData(MergeBatchStatus.Pending, MergeBatchStatus.Abandoned, true)]
    [InlineData(MergeBatchStatus.InProgress, MergeBatchStatus.Merged, true)]
    [InlineData(MergeBatchStatus.InProgress, MergeBatchStatus.Abandoned, true)]
    [InlineData(MergeBatchStatus.InProgress, MergeBatchStatus.Pending, false)]
    [InlineData(MergeBatchStatus.Merged, MergeBatchStatus.Pending, false)]
    [InlineData(MergeBatchStatus.Merged, MergeBatchStatus.InProgress, false)]
    [InlineData(MergeBatchStatus.Merged, MergeBatchStatus.Abandoned, false)]
    [InlineData(MergeBatchStatus.Abandoned, MergeBatchStatus.Pending, false)]
    [InlineData(MergeBatchStatus.Abandoned, MergeBatchStatus.InProgress, false)]
    [InlineData(MergeBatchStatus.Abandoned, MergeBatchStatus.Merged, false)]
    public void CheckLegalTransitions(MergeBatchStatus from, MergeBatchStatus to, bool legal)
    {
        MergeBatchTransitions.IsLegal(from, to).ShouldBe(legal);
    }

    [Fact(DisplayName = "Given Merged status, when TargetsFrom is called, then it is empty (terminal)")]
    public void MergedIsTerminal()
    {
        MergeBatchTransitions.TargetsFrom(MergeBatchStatus.Merged).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given Abandoned status, when TargetsFrom is called, then it is empty (terminal)")]
    public void AbandonedIsTerminal()
    {
        MergeBatchTransitions.TargetsFrom(MergeBatchStatus.Abandoned).ShouldBeEmpty();
    }
}
