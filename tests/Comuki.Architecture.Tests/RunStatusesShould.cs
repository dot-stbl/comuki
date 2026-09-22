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
    [Theory(DisplayName = "Given a RunStatus member, when compared to RunStatuses, then the constant equals the smart-type member name")]
    [MemberData(nameof(StatusKeys))]
    public void MatchTheEnumMemberName(string enumMemberName, string key)
    {
        key.ShouldBe(enumMemberName);
    }

    /// <summary>
    /// Pairs every <see cref="RunStatus"/> static member name with the matching
    /// <see cref="RunStatuses"/> constant. <see cref="MemberDataAttribute"/>
    /// because <see cref="InlineDataAttribute"/> requires constants and
    /// <c>RunStatus.X</c> members are static properties.
    /// </summary>
    public static TheoryData<string, string> StatusKeys
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var status in RunStatus.All)
            {
                var name = status.Value;
                switch (name)
                {
                    case "Queued": data.Add(name, RunStatuses.Queued); break;
                    case "Waiting": data.Add(name, RunStatuses.Waiting); break;
                    case "Running": data.Add(name, RunStatuses.Running); break;
                    case "Succeeded": data.Add(name, RunStatuses.Succeeded); break;
                    case "Failed": data.Add(name, RunStatuses.Failed); break;
                    case "Cancelled": data.Add(name, RunStatuses.Cancelled); break;
                    case "Escalated": data.Add(name, RunStatuses.Escalated); break;
                }
            }

            return data;
        }
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

        // Smart-type no longer supports Enum.GetNames; All enumerates every
        // working member in declaration order (excludes Unspecified).
        keys.ShouldBe([.. RunStatus.All.Select(static status => status.Value)], ignoreOrder: true);
    }
}
