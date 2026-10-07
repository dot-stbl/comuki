using Comuki.Host.Integration;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Cli.Integration;

/// <summary>
/// Production defaults for the integrations worker config: the issue default
/// profile key points at the <c>implement</c> profile (the only one
/// backed by a control-plane profile today).
/// </summary>
public sealed class IntegrationWorkerDefaultsShould
{
    [Fact(DisplayName = "Given the production defaults, when constructed, then the issue profile key is implement")]
    public void DefaultIssueProfileKeyIsImplement()
    {
        new IntegrationWorkerDefaults().IssueDefaultProfileKey.ShouldBe("implement");
    }
}
