using Comuki.Modules.Chat.Application.Graph.Confirm;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Plan-gate extraction cases: the brain sometimes prefixes its final JSON
/// with prose (a trailing thinking fragment) — the gate must recover the
/// outermost JSON object instead of rejecting the whole payload.
/// </summary>
public sealed class ChatPlanGateShould
{
    private const string ValidPlanJson =
                             /*lang=json,strict*/
                             """{"summary":"append the line","nodes":[{"id":"n1","title":"Append","profileKey":"implement","brief":"append E2E Slice 0 OK"}],"edges":[]}""";

    [Fact(DisplayName = "Given a clean JSON payload, when Validate runs, then the plan passes through")]
    public void CleanJsonPasses()
    {
        var outcome = ChatPlanGate.Validate(ValidPlanJson);

        outcome.Plan.ShouldNotBeNull();
        outcome.Explanation.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a prose-prefixed JSON payload, when Validate runs, then the embedded plan is recovered")]
    public void PrefixedJsonIsRecovered()
    {
        var payload = "_thinking_\niteration 1: list_profiles\niteration 2: emit_plan\n\n" + ValidPlanJson;

        var outcome = ChatPlanGate.Validate(payload);

        outcome.Plan.ShouldNotBeNull();
        outcome.Explanation.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given pure prose without any JSON, when Validate runs, then the payload is the explanation")]
    public void PureProseIsExplanation()
    {
        var outcome = ChatPlanGate.Validate("The task is ambiguous; refine the request.");

        outcome.Plan.ShouldBeNull();
        outcome.Explanation.ShouldBe("The task is ambiguous; refine the request.");
    }
}
