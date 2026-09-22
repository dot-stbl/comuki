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
    [MemberData(nameof(LegalTransitionMatrix))]
    public void CheckLegalTransitions(MergeBatchStatus from, MergeBatchStatus to, bool legal)
    {
        MergeBatchTransitions.IsLegal(from, to).ShouldBe(legal);
    }

    /// <summary>
    /// Full <c>(from, to, legal)</c> matrix — <see cref="MemberDataAttribute"/>
    /// because <see cref="InlineDataAttribute"/> requires constants and
    /// <see cref="MergeBatchStatus"/> members are static properties.
    /// </summary>
    public static TheoryData<MergeBatchStatus, MergeBatchStatus, bool> LegalTransitionMatrix
    {
        get
        {
            var data = new TheoryData<MergeBatchStatus, MergeBatchStatus, bool>
            {
                { MergeBatchStatus.Pending, MergeBatchStatus.InProgress, true },
                { MergeBatchStatus.Pending, MergeBatchStatus.Merged, false },
                { MergeBatchStatus.Pending, MergeBatchStatus.Abandoned, true },
                { MergeBatchStatus.InProgress, MergeBatchStatus.Merged, true },
                { MergeBatchStatus.InProgress, MergeBatchStatus.Abandoned, true },
                { MergeBatchStatus.InProgress, MergeBatchStatus.Pending, false },
                { MergeBatchStatus.Merged, MergeBatchStatus.Pending, false },
                { MergeBatchStatus.Merged, MergeBatchStatus.InProgress, false },
                { MergeBatchStatus.Merged, MergeBatchStatus.Abandoned, false },
                { MergeBatchStatus.Abandoned, MergeBatchStatus.Pending, false },
                { MergeBatchStatus.Abandoned, MergeBatchStatus.InProgress, false },
                { MergeBatchStatus.Abandoned, MergeBatchStatus.Merged, false },
            };
            return data;
        }
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
