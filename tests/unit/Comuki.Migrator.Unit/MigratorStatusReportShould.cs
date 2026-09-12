using Comuki.Migrator.Status;
using Shouldly;
using Xunit;

namespace Comuki.Migrator.Unit;

/// <summary>
/// <c>comuki-migrator status</c> dry-run contract (issue #56 §3):
/// pending lines mirror the applied style, up-to-date schemas get their
/// line, exit codes 0/1 distinguish applied vs pending (2 is the error
/// path the Program wrapper owns).
/// </summary>
public sealed class MigratorStatusReportShould
{
    [Fact(DisplayName = "Given a schema with pending migrations, when rendered, then each pending migration gets a pending line")]
    public void RenderPendingLines()
    {
        var lines = MigratorStatusReport.RenderLines(new MigratorSchemaStatus(
            "orchestration",
            ["20260911062800_Init", "20260912090000_AddRuns"])).ToArray();

        lines.ShouldBe(
        [
            "pending (orchestration): 20260911062800_Init",
            "pending (orchestration): 20260912090000_AddRuns",
        ]);
    }

    [Fact(DisplayName = "Given a schema without pending migrations, when rendered, then the up-to-date line appears")]
    public void RenderUpToDateLine()
    {
        var lines = MigratorStatusReport.RenderLines(new MigratorSchemaStatus("identity", [])).ToArray();

        lines.ShouldBe(["identity schema is up to date"]);
    }

    [Theory(DisplayName = "Given the total pending count, when the exit code is computed, then 0 means applied and 1 means pending")]
    [InlineData(0, MigratorStatusReport.UpToDateExitCode)]
    [InlineData(1, MigratorStatusReport.PendingExitCode)]
    [InlineData(7, MigratorStatusReport.PendingExitCode)]
    public void ExitCodeFollowsPendingCount(int totalPending, int expected)
    {
        MigratorStatusReport.ExitCode(totalPending).ShouldBe(expected);
    }

    [Fact(DisplayName = "Given the exit-code contract, when CI consumes it, then the codes are 0/1/2")]
    public void ExitCodeContractIsStable()
    {
        MigratorStatusReport.UpToDateExitCode.ShouldBe(0);
        MigratorStatusReport.PendingExitCode.ShouldBe(1);
        MigratorStatusReport.ErrorExitCode.ShouldBe(2);
    }
}
