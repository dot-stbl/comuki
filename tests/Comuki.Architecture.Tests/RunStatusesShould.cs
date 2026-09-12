using Comuki.Engine.Orchestration.Domain;
using Comuki.Shared.Contracts.Runs;
using Shouldly;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Drift guard for <see cref="RunStatuses"/> — Intake's compile-checked
/// stand-in for <see cref="RunStatus"/>, kept in Shared.Contracts because
/// Intake must not reference the engine (see the module-boundary tests).
/// This project is the one place both assemblies are visible together, so
/// it is where a rename on either side gets caught: renaming a
/// <see cref="RunStatus"/> member breaks the <c>nameof</c> below at
/// compile time; renaming, removing, or drifting the value of a
/// <see cref="RunStatuses"/> constant fails the assertions. Either way the
/// build stops before Intake's tracker sync-back quietly stops matching
/// rows in Postgres.
/// </summary>
public sealed class RunStatusesShould
{
    [Theory(DisplayName = "Given a RunStatus member, when compared to RunStatuses, then the constant equals the enum member name")]
    [InlineData(nameof(RunStatus.Queued), RunStatuses.Queued)]
    [InlineData(nameof(RunStatus.Waiting), RunStatuses.Waiting)]
    [InlineData(nameof(RunStatus.Running), RunStatuses.Running)]
    [InlineData(nameof(RunStatus.Succeeded), RunStatuses.Succeeded)]
    [InlineData(nameof(RunStatus.Failed), RunStatuses.Failed)]
    [InlineData(nameof(RunStatus.Cancelled), RunStatuses.Cancelled)]
    [InlineData(nameof(RunStatus.Escalated), RunStatuses.Escalated)]
    public void MatchTheEnumMemberName(string enumMemberName, string key)
    {
        key.ShouldBe(enumMemberName);
    }

    [Fact(DisplayName = "Given every RunStatus member, when enumerated, then RunStatuses defines a matching constant")]
    public void DefineAConstantForEveryEnumMember()
    {
        string[] keys =
        [
            RunStatuses.Queued,
            RunStatuses.Waiting,
            RunStatuses.Running,
            RunStatuses.Succeeded,
            RunStatuses.Failed,
            RunStatuses.Cancelled,
            RunStatuses.Escalated,
        ];

        keys.ShouldBe(Enum.GetNames<RunStatus>(), ignoreOrder: true);
    }
}
