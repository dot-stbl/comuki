using Comuki.Modules.Work.Domain;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Full-matrix test for <see cref="WorkTaskTransitions"/> —
/// the legal transition table the aggregate guard and the Application
/// <c>WorkTaskStatusMachine</c> read. The expected table below is the
/// spec — production <see cref="WorkTaskTransitions"/> must
/// match it pair-for-pair.
/// </summary>
public sealed class WorkTaskTransitionsShould
{
    private static readonly IReadOnlyDictionary<WorkTaskStatus, WorkTaskStatus[]> expectedTransitions =
        new Dictionary<WorkTaskStatus, WorkTaskStatus[]>
        {
            [WorkTaskStatus.Draft] = [WorkTaskStatus.Ready, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Ready] = [WorkTaskStatus.Active, WorkTaskStatus.Blocked, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Active] = [WorkTaskStatus.Blocked, WorkTaskStatus.Resolved, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Blocked] = [WorkTaskStatus.Ready, WorkTaskStatus.Resolved, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Resolved] = [],
            [WorkTaskStatus.Cancelled] = [],
        };

    public static TheoryData<WorkTaskStatus, WorkTaskStatus, bool> Matrix
    {
        get
        {
            var data = new TheoryData<WorkTaskStatus, WorkTaskStatus, bool>();
            foreach (var from in WorkTaskStatus.All)
            {
                foreach (var to in WorkTaskStatus.All)
                {
                    data.Add(from, to, expectedTransitions[from].Contains(to));
                }
            }

            return data;
        }
    }

    [Theory(DisplayName = "Given task statuses from/to, when IsLegal is called, then it matches the transition table")]
    [MemberData(nameof(Matrix))]
    public void MatchTransitionTable(WorkTaskStatus from, WorkTaskStatus to, bool expected)
    {
        WorkTaskTransitions.IsLegal(from, to).ShouldBe(expected);
    }

    [Fact(DisplayName = "Given every status, when TargetsFrom is called, then the targets match the table")]
    public void ReturnTargetsFromForEveryStatus()
    {
        foreach (var from in WorkTaskStatus.All)
        {
            WorkTaskTransitions.TargetsFrom(from).ShouldBe(expectedTransitions[from], ignoreOrder: true);
        }
    }

    [Fact(DisplayName = "Given Resolved, when TargetsFrom is called, then the set is empty")]
    public void ResolvedHasNoOutgoingEdges()
    {
        WorkTaskTransitions.TargetsFrom(WorkTaskStatus.Resolved).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given Cancelled, when TargetsFrom is called, then the set is empty")]
    public void CancelledHasNoOutgoingEdges()
    {
        WorkTaskTransitions.TargetsFrom(WorkTaskStatus.Cancelled).ShouldBeEmpty();
    }
}
