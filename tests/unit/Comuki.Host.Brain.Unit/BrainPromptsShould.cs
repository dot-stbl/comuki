using Comuki.Host.Brain.Brain;
using Comuki.Shared.Contracts.Brain;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// System prompt selection per request kind — the plan prompt pins the
/// emit_plan protocol, unknown kinds are refused.
/// </summary>
public sealed class BrainPromptsShould
{
    [Theory(DisplayName = "Given a kind key, when For is called, then the matching prompt comes back")]
    [InlineData(BrainRequestKindKeys.Plan)]
    [InlineData(BrainRequestKindKeys.Brief)]
    [InlineData(BrainRequestKindKeys.Repair)]
    [InlineData(BrainRequestKindKeys.Answer)]
    public void MapKindToPrompt(string kind)
    {
        var prompt = BrainPrompts.For(kind);

        prompt.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "Given the plan prompt, when read, then it pins the plan shape, a few-shot example and the emit_plan call")]
    public void PinPlanProtocol()
    {
        BrainPrompts.Plan.ShouldContain("emit_plan");
        BrainPrompts.Plan.ShouldContain("\"profileKey\"");
        BrainPrompts.Plan.ShouldContain("acyclic");
        BrainPrompts.Plan.ShouldContain("non-empty");

        // few-shot example: a complete valid plan with real profile keys
        BrainPrompts.Plan.ShouldContain("Example of a valid plan");
        BrainPrompts.Plan.ShouldContain("\"id\": \"step-1\"");
        BrainPrompts.Plan.ShouldContain("\"from\": \"step-1\", \"to\": \"step-2\"");
    }

    [Fact(DisplayName = "Given an unknown kind, when For is called, then ArgumentOutOfRangeException")]
    public void RefuseUnknownKind()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => BrainPrompts.For("dream"));
    }
}
