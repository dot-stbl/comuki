using Comuki.Host.Intake;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Cli.Intake;

/// <summary>
/// Production defaults for the intake worker config: the issue default
/// profile key points at the <c>implement</c> profile (the only one
/// backed by a control-plane profile today).
/// </summary>
public sealed class IntakeWorkerDefaultsShould
{
    [Fact(DisplayName = "Given the production defaults, when constructed, then the issue profile key is implement")]
    public void DefaultIssueProfileKeyIsImplement()
    {
        new IntakeWorkerDefaults().IssueDefaultProfileKey.ShouldBe("implement");
    }
}
